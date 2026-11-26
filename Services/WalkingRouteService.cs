using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace RowadUmrahSystem.Web.Services;

/// <summary>
/// Le trajet a pied entre le pelerin et un lieu.
///
/// Deux fournisseurs :
///   - Google Routes API si une cle est configuree (plus precis, payant) ;
///   - OSRM (Open Source Routing Machine, donnees OpenStreetMap) sinon (gratuit, sans cle).
///
/// Le choix est automatique : avec une cle on passe par Google, sans cle on passe par OSRM.
/// Le format de sortie est le meme (polyline encodee) : l'application ne voit aucune difference.
///
/// CE QUE CE SERVICE NE FAIT PAS
/// Il ne guide pas. Pas d'instructions vocales, pas de recalcul quand on tourne au mauvais coin.
/// Il rend le trace, la distance et la duree a pied, une fois. L'application dessine ce trace
/// sur sa carte ; elle ne pretend pas accompagner le pelerin pas a pas.
/// </summary>
public sealed record RouteStep(string Instruction, string? StreetName, double DistanceMeters, double DurationSeconds, string ManeuverType);

public sealed record WalkingRoute(int DistanceMeters, int DurationSeconds, string EncodedPolyline, IReadOnlyList<RouteStep>? Steps);

public sealed record WalkingRouteResult(PlaceSource Source, WalkingRoute? Route, int RemainingToday);

public interface IWalkingRouteService
{
    bool IsConfigured { get; }
    Task<WalkingRouteResult> RouteAsync(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, CancellationToken ct);
}

public sealed class WalkingRouteService : IWalkingRouteService
{
    private readonly HttpClient _google;
    private readonly HttpClient _osrm;
    private readonly PlaceSearchOptions _settings;
    private readonly ILogger<WalkingRouteService> _log;

    public WalkingRouteService(
        IHttpClientFactory httpFactory,
        IOptions<PlaceSearchOptions> options,
        ILogger<WalkingRouteService> log)
    {
        _google = httpFactory.CreateClient("GoogleRoutes");
        _osrm = httpFactory.CreateClient("Osrm");
        _settings = options.Value;
        _log = log;
    }

    public bool IsConfigured => true;

    private bool GoogleConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    private static readonly object counterLock = new();
    private static DateOnly counterDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private static int callsToday;

    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expires, WalkingRoute Route)> cache = new();

    private bool TryReserveGoogleCall(out int remaining)
    {
        lock (counterLock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != counterDay)
            {
                counterDay = today;
                callsToday = 0;
            }
            if (callsToday >= _settings.RouteDailyRequestLimit)
            {
                remaining = 0;
                return false;
            }
            callsToday++;
            remaining = _settings.RouteDailyRequestLimit - callsToday;
            return true;
        }
    }

    private int RemainingNow()
    {
        lock (counterLock)
        {
            if (DateOnly.FromDateTime(DateTime.UtcNow) != counterDay) return _settings.RouteDailyRequestLimit;
            return Math.Max(0, _settings.RouteDailyRequestLimit - callsToday);
        }
    }

    private static string CacheKey(double fromLat, double fromLng, double toLat, double toLng) => string.Create(
        CultureInfo.InvariantCulture,
        $"{Math.Round(fromLat, 4)},{Math.Round(fromLng, 4)}>{Math.Round(toLat, 4)},{Math.Round(toLng, 4)}");

    public async Task<WalkingRouteResult> RouteAsync(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, CancellationToken ct)
    {
        var key = CacheKey(fromLatitude, fromLongitude, toLatitude, toLongitude);
        if (cache.TryGetValue(key, out var hit) && hit.Expires > DateTimeOffset.UtcNow)
        {
            return new WalkingRouteResult(PlaceSource.Cache, hit.Route, RemainingNow());
        }

        if (GoogleConfigured)
        {
            var google = await RouteViaGoogleAsync(key, fromLatitude, fromLongitude, toLatitude, toLongitude, ct);
            if (google.Route is not null) return google;
            _log.LogInformation("Google indisponible, bascule vers OSRM.");
        }

        return await RouteViaOsrmAsync(key, fromLatitude, fromLongitude, toLatitude, toLongitude, ct);
    }

    // ── Google Routes API ──────────────────────────────────────────────

    private async Task<WalkingRouteResult> RouteViaGoogleAsync(
        string key, double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct)
    {
        if (!TryReserveGoogleCall(out var remaining))
        {
            _log.LogWarning("Trajet a pied : plafond quotidien de {Limit} appels atteint.", _settings.RouteDailyRequestLimit);
            return new WalkingRouteResult(PlaceSource.QuotaReached, null, 0);
        }

        try
        {
            var route = await CallGoogleAsync(fromLat, fromLng, toLat, toLng, ct);
            if (route is null) return new WalkingRouteResult(PlaceSource.Unavailable, null, remaining);
            cache[key] = (DateTimeOffset.UtcNow.AddHours(_settings.CacheHours), route);
            return new WalkingRouteResult(PlaceSource.Google, route, remaining);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _log.LogError(error, "Trajet a pied : appel a Google en echec.");
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
        request.Headers.Add("X-Goog-Api-Key", _settings.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", "routes.distanceMeters,routes.duration,routes.polyline.encodedPolyline");

        using var response = await _google.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Google a repondu {(int)response.StatusCode} : {detail}");
        }

        var payload = await response.Content.ReadFromJsonAsync<RoutesResponse>(cancellationToken: ct);
        return MapGoogle(payload);
    }

    public static WalkingRoute? MapGoogle(RoutesResponse? payload)
    {
        var route = payload?.Routes?.FirstOrDefault();
        var encoded = route?.Polyline?.EncodedPolyline;
        if (route is null || string.IsNullOrWhiteSpace(encoded)) return null;
        return new WalkingRoute(route.DistanceMeters, ParseDuration(route.Duration), encoded, null);
    }

    public static int ParseDuration(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return 0;
        var digits = duration.TrimEnd('s', 'S');
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) ? seconds : 0;
    }

    // ── OSRM (gratuit, donnees OpenStreetMap) ──────────────────────────

    private async Task<WalkingRouteResult> RouteViaOsrmAsync(
        string key, double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct)
    {
        try
        {
            var route = await CallOsrmAsync(fromLat, fromLng, toLat, toLng, ct);
            if (route is null) return new WalkingRouteResult(PlaceSource.Unavailable, null, 0);
            cache[key] = (DateTimeOffset.UtcNow.AddHours(_settings.CacheHours), route);
            return new WalkingRouteResult(PlaceSource.OpenStreetMap, route, 0);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _log.LogError(error, "Trajet a pied : appel a OSRM en echec.");
            return new WalkingRouteResult(PlaceSource.Unavailable, null, 0);
        }
    }

    private async Task<WalkingRoute?> CallOsrmAsync(double fromLat, double fromLng, double toLat, double toLng, CancellationToken ct)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"route/v1/foot/{fromLng},{fromLat};{toLng},{toLat}?overview=full&geometries=polyline&steps=true");

        using var response = await _osrm.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return null;

        var payload = await response.Content.ReadFromJsonAsync<OsrmResponse>(cancellationToken: ct);
        return MapOsrm(payload);
    }

    public static WalkingRoute? MapOsrm(OsrmResponse? payload)
    {
        if (payload?.Code != "Ok") return null;
        var route = payload.Routes?.FirstOrDefault();
        if (route is null || string.IsNullOrWhiteSpace(route.Geometry)) return null;

        var steps = new List<RouteStep>();
        var leg = route.Legs?.FirstOrDefault();
        if (leg?.Steps is not null)
        {
            foreach (var step in leg.Steps)
            {
                if (step.Distance < 1 && step.Maneuver?.Type == "arrive") continue;
                var instruction = OsrmInstruction(step.Maneuver?.Type, step.Maneuver?.Modifier, step.Name);
                steps.Add(new RouteStep(instruction, step.Name, step.Distance, step.Duration, step.Maneuver?.Type ?? "turn"));
            }
        }

        return new WalkingRoute(
            (int)Math.Round(route.Distance),
            (int)Math.Round(route.Duration),
            route.Geometry,
            steps.Count > 0 ? steps : null);
    }

    private static string OsrmInstruction(string? type, string? modifier, string? street)
    {
        var on = string.IsNullOrWhiteSpace(street) ? "" : $" في {street}";
        return (type, modifier) switch
        {
            ("depart", _) => $"ابدأ المشي{on}",
            ("arrive", _) => "وصلت إلى وجهتك",
            ("turn", "left" or "slight left" or "sharp left") => $"انعطف يساراً{on}",
            ("turn", "right" or "slight right" or "sharp right") => $"انعطف يميناً{on}",
            ("turn", "uturn") => "استدر للخلف",
            ("new name" or "continue", _) => $"تابع المشي{on}",
            ("end of road", "left" or "slight left" or "sharp left") => $"عند نهاية الطريق انعطف يساراً{on}",
            ("end of road", _) => $"عند نهاية الطريق انعطف يميناً{on}",
            ("roundabout" or "rotary", _) => $"ادخل الدوار{on}",
            ("fork", "left" or "slight left" or "sharp left") => $"خذ المسار الأيسر{on}",
            ("fork", _) => $"خذ المسار الأيمن{on}",
            _ => $"تابع{on}"
        };
    }

    // ── Modeles de reponse ─────────────────────────────────────────────

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

    public sealed class OsrmResponse
    {
        [JsonPropertyName("code")] public string? Code { get; set; }
        [JsonPropertyName("routes")] public List<OsrmRoute>? Routes { get; set; }
    }

    public sealed class OsrmRoute
    {
        [JsonPropertyName("geometry")] public string? Geometry { get; set; }
        [JsonPropertyName("distance")] public double Distance { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("legs")] public List<OsrmLeg>? Legs { get; set; }
    }

    public sealed class OsrmLeg
    {
        [JsonPropertyName("steps")] public List<OsrmStep>? Steps { get; set; }
    }

    public sealed class OsrmStep
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("distance")] public double Distance { get; set; }
        [JsonPropertyName("duration")] public double Duration { get; set; }
        [JsonPropertyName("maneuver")] public OsrmManeuver? Maneuver { get; set; }
    }

    public sealed class OsrmManeuver
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("modifier")] public string? Modifier { get; set; }
    }
}
