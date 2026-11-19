using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RowadUmrahSystem.Web.Services;

/// <summary>
/// Le trajet a pied entre le pelerin et un lieu, via l'API Routes de Google.
///
/// POURQUOI SUR LE SERVEUR, COMME LA RECHERCHE
/// Meme raison : la cle ne doit pas sortir, et le plafond de depense ne peut etre tenu que la ou
/// l'on compte les appels. Un trajet coute un appel facture.
///
/// CE QUE CE SERVICE NE FAIT PAS
/// Il ne guide pas. Pas d'instructions vocales, pas de recalcul quand on tourne au mauvais coin.
/// Il rend le trace, la distance et la duree a pied, une fois. L'application dessine ce trace
/// sur sa carte ; elle ne pretend pas accompagner le pelerin pas a pas.
/// </summary>
public sealed record WalkingRoute(int DistanceMeters, int DurationSeconds, string EncodedPolyline);

public sealed record WalkingRouteResult(PlaceSource Source, WalkingRoute? Route, int RemainingToday);

public interface IWalkingRouteService
{
    bool IsConfigured { get; }
    Task<WalkingRouteResult> RouteAsync(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, CancellationToken ct);
}

public sealed class WalkingRouteService(HttpClient client, IOptions<PlaceSearchOptions> options, ILogger<WalkingRouteService> log)
    : IWalkingRouteService
{
    private readonly PlaceSearchOptions settings = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.ApiKey);

    // Compteur propre aux trajets : si quelqu'un epuise les trajets, la recherche de lieux
    // continue de fonctionner, et inversement. Deux services, deux budgets.
    private static readonly object counterLock = new();
    private static DateOnly counterDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private static int callsToday;

    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expires, WalkingRoute Route)> cache = new();

    private bool TryReserveCall(out int remaining)
    {
        lock (counterLock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != counterDay)
            {
                counterDay = today;
                callsToday = 0;
            }
            if (callsToday >= settings.RouteDailyRequestLimit)
            {
                remaining = 0;
                return false;
            }
            callsToday++;
            remaining = settings.RouteDailyRequestLimit - callsToday;
            return true;
        }
    }

    private int RemainingNow()
    {
        lock (counterLock)
        {
            if (DateOnly.FromDateTime(DateTime.UtcNow) != counterDay) return settings.RouteDailyRequestLimit;
            return Math.Max(0, settings.RouteDailyRequestLimit - callsToday);
        }
    }

    /// <summary>
    /// La cle du cache, arrondie a quatre decimales — environ onze metres. Deux demandes faites
    /// depuis le meme trottoir vers le meme hotel ne valent pas deux appels factures.
    /// </summary>
    private static string CacheKey(double fromLat, double fromLng, double toLat, double toLng) => string.Create(
        CultureInfo.InvariantCulture,
        $"{Math.Round(fromLat, 4)},{Math.Round(fromLng, 4)}>{Math.Round(toLat, 4)},{Math.Round(toLng, 4)}");

    public async Task<WalkingRouteResult> RouteAsync(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, CancellationToken ct)
    {
        if (!IsConfigured) return new WalkingRouteResult(PlaceSource.NotConfigured, null, 0);

        var key = CacheKey(fromLatitude, fromLongitude, toLatitude, toLongitude);
        if (cache.TryGetValue(key, out var hit) && hit.Expires > DateTimeOffset.UtcNow)
        {
            return new WalkingRouteResult(PlaceSource.Cache, hit.Route, RemainingNow());
        }

        if (!TryReserveCall(out var remaining))
        {
            log.LogWarning("Trajet a pied : plafond quotidien de {Limit} appels atteint.", settings.RouteDailyRequestLimit);
            return new WalkingRouteResult(PlaceSource.QuotaReached, null, 0);
        }

        try
        {
            var route = await CallGoogleAsync(fromLatitude, fromLongitude, toLatitude, toLongitude, ct);
            if (route is null) return new WalkingRouteResult(PlaceSource.Unavailable, null, remaining);
            cache[key] = (DateTimeOffset.UtcNow.AddHours(settings.CacheHours), route);
            return new WalkingRouteResult(PlaceSource.Google, route, remaining);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            log.LogError(error, "Trajet a pied : appel a Google en echec.");
            return new WalkingRouteResult(PlaceSource.Unavailable, null, remaining);
        }
    }

    private async Task<WalkingRoute?> CallGoogleAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct)
    {
        var body = new
        {
            origin = new { location = new { latLng = new { latitude = fromLat, longitude = fromLng } } },
            destination = new { location = new { latLng = new { latitude = toLat, longitude = toLng } } },
            travelMode = "WALK",
            languageCode = "ar",
            units = "METRIC"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "directions/v2:computeRoutes")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Goog-Api-Key", settings.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Google a repondu {(int)response.StatusCode} : {detail}");
        }

        var payload = await response.Content.ReadFromJsonAsync<RoutesResponse>(cancellationToken: ct);
        return Map(payload);
    }

    /// <summary>Traduit la reponse de Google. Visible pour les tests.</summary>
    public static WalkingRoute? Map(RoutesResponse? payload)
    {
        var route = payload?.Routes?.FirstOrDefault();
        var encoded = route?.Polyline?.EncodedPolyline;
        if (route is null || string.IsNullOrWhiteSpace(encoded)) return null;
        return new WalkingRoute(route.DistanceMeters, ParseDuration(route.Duration), encoded);
    }

    /// <summary>Google rend la duree sous la forme « 705s ». Une valeur illisible vaut zero.</summary>
    public static int ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return 0;
        var digits = duration.TrimEnd('s', 'S');
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
    }

    public sealed class RoutesResponse
    {
        [JsonPropertyName("routes")] public List<RouteEntry>? Routes { get; set; }
    }

    public sealed class RouteEntry
    {
        [JsonPropertyName("distanceMeters")] public int DistanceMeters { get; set; }
        [JsonPropertyName("duration")] public string? Duration { get; set; }
        [JsonPropertyName("polyline")] public PolylineEntry? Polyline { get; set; }
    }

    public sealed class PolylineEntry
    {
        [JsonPropertyName("encodedPolyline")] public string? EncodedPolyline { get; set; }
    }
}
