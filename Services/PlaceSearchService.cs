using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace RowadUmrahSystem.Web.Services;

/// <summary>
/// Recherche de lieux reels autour du pelerin, via l'API Google Places.
///
/// POURQUOI CE CODE EST SUR LE SERVEUR ET PAS DANS L'APPLICATION
/// Une cle posee dans l'APK est lisible par n'importe qui : il suffit d'ouvrir le fichier. Elle
/// serait alors utilisee par d'autres, sur la facture de l'agence. Ici la cle ne quitte jamais le
/// serveur, et c'est le serveur qui compte les appels.
///
/// LE BUDGET
/// Chaque appel est payant au-dela du credit mensuel offert. DailyRequestLimit est un plafond
/// DUR : une fois atteint, on ne parle plus a Google de la journee et l'application retombe sur
/// sa liste embarquee. Un plafond cote serveur est la seule facon de garantir la depense ;
/// une alerte de budget, elle, previent apres coup sans rien arreter.
/// </summary>
public sealed class PlaceSearchOptions
{
    public string ApiKey { get; set; } = "";
    /// <summary>Plafond d'appels payants par jour. Au-dela, la recherche distante est coupee.</summary>
    public int DailyRequestLimit { get; set; } = 300;
    /// <summary>Plafond d'appels de trajet par jour. Compte a part de la recherche de lieux.</summary>
    public int RouteDailyRequestLimit { get; set; } = 200;
    /// <summary>Duree de conservation d'un resultat, en heures. Un resultat garde ne coute rien.</summary>
    public int CacheHours { get; set; } = 12;
    /// <summary>Rayon de recherche autour du point donne, en metres.</summary>
    public int RadiusMeters { get; set; } = 3000;
}

/// <summary>Un lieu, dans la forme exacte que l'application attend deja.</summary>
public sealed record PlaceDto(
    string Id,
    string City,
    string Category,
    string Name,
    PlaceCoordinate Coordinate,
    string? Address);

public sealed record PlaceCoordinate(double Latitude, double Longitude);

/// <summary>
/// D'ou viennent les lieux rendus. L'application en a besoin pour savoir quoi dire : « resultats
/// reels » ou « liste embarquee ». Elle ne doit jamais faire croire a des lieux verifies quand
/// elle n'a que sa liste hors ligne.
/// </summary>
public enum PlaceSource
{
    /// <summary>Appel fait a Google a l'instant.</summary>
    Google,
    /// <summary>Resultat deja obtenu, garde en memoire : aucun appel, aucun cout.</summary>
    Cache,
    /// <summary>Aucune cle configuree.</summary>
    NotConfigured,
    /// <summary>Plafond du jour atteint : on ne depense plus.</summary>
    QuotaReached,
    /// <summary>Google n'a pas repondu correctement.</summary>
    Unavailable
}

public sealed record PlaceSearchResult(PlaceSource Source, IReadOnlyList<PlaceDto> Places, int RemainingToday);

public sealed record PlaceSearchQuery(string Query, string? Category, string City, double Latitude, double Longitude);

public interface IPlaceSearchService
{
    bool IsConfigured { get; }
    Task<PlaceSearchResult> SearchAsync(PlaceSearchQuery query, CancellationToken ct);
}

public sealed class PlaceSearchService(HttpClient client, IOptions<PlaceSearchOptions> options, ILogger<PlaceSearchService> log)
    : IPlaceSearchService
{
    private readonly PlaceSearchOptions settings = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(settings.ApiKey);

    // Le compteur et le cache vivent dans le processus : ils repartent de zero a chaque
    // redemarrage. C'est volontaire et sans danger — repartir a zero ne fait que REDONNER des
    // appels, et le plafond reste tres en dessous du credit mensuel offert.
    private static readonly object counterLock = new();
    private static DateOnly counterDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private static int callsToday;

    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expires, IReadOnlyList<PlaceDto> Places)> cache = new();

    /// <summary>
    /// Les categories de l'application, et les types Google qui leur correspondent. Un type
    /// inconnu ne devient PAS « autre » : le lieu est ecarte. Mieux vaut montrer moins de lieux
    /// que de ranger une banque dans les pharmacies.
    /// </summary>
    private static readonly (string Category, string[] Types)[] CategoryTypes =
    [
        ("hotels", ["lodging", "hotel", "motel", "guest_house", "resort_hotel", "extended_stay_hotel"]),
        ("restaurants", ["restaurant", "cafe", "bakery", "meal_takeaway", "meal_delivery", "coffee_shop", "fast_food_restaurant"]),
        ("mosques", ["mosque", "place_of_worship"]),
        ("health", ["pharmacy", "drugstore", "hospital", "doctor", "medical_lab", "dental_clinic"]),
        ("exchange", ["bank", "atm", "money_transfer", "currency_exchange", "accounting"]),
        ("transport", ["bus_station", "bus_stop", "transit_station", "taxi_stand", "parking", "train_station", "airport"])
    ];

    private static string? CategoryOf(IEnumerable<string>? types)
    {
        if (types is null) return null;
        var set = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, candidates) in CategoryTypes)
        {
            if (candidates.Any(set.Contains)) return category;
        }
        return null;
    }

    /// <summary>Reserve un appel payant, ou refuse si le plafond du jour est atteint.</summary>
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
            if (callsToday >= settings.DailyRequestLimit)
            {
                remaining = 0;
                return false;
            }
            callsToday++;
            remaining = settings.DailyRequestLimit - callsToday;
            return true;
        }
    }

    private static int RemainingNow(int limit)
    {
        lock (counterLock)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != counterDay) return limit;
            return Math.Max(0, limit - callsToday);
        }
    }

    /// <summary>La cle du cache. Les coordonnees sont arrondies : a 500 m pres, c'est la meme recherche.</summary>
    private static string CacheKey(PlaceSearchQuery query) => string.Create(
        CultureInfo.InvariantCulture,
        $"{query.Query.Trim().ToLowerInvariant()}|{query.Category}|{query.City}|{Math.Round(query.Latitude, 2)}|{Math.Round(query.Longitude, 2)}");

    public async Task<PlaceSearchResult> SearchAsync(PlaceSearchQuery query, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return new PlaceSearchResult(PlaceSource.NotConfigured, [], 0);
        }

        var key = CacheKey(query);
        if (cache.TryGetValue(key, out var hit) && hit.Expires > DateTimeOffset.UtcNow)
        {
            return new PlaceSearchResult(PlaceSource.Cache, hit.Places, RemainingNow(settings.DailyRequestLimit));
        }

        if (!TryReserveCall(out var remaining))
        {
            log.LogWarning("Recherche de lieux : plafond quotidien de {Limit} appels atteint.", settings.DailyRequestLimit);
            return new PlaceSearchResult(PlaceSource.QuotaReached, [], 0);
        }

        try
        {
            var places = await CallGoogleAsync(query, ct);
            cache[key] = (DateTimeOffset.UtcNow.AddHours(settings.CacheHours), places);
            return new PlaceSearchResult(PlaceSource.Google, places, remaining);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // L'appel est deja compte : Google a pu le facturer meme si la reponse s'est perdue.
            log.LogError(error, "Recherche de lieux : appel a Google en echec.");
            return new PlaceSearchResult(PlaceSource.Unavailable, [], remaining);
        }
    }

    private async Task<IReadOnlyList<PlaceDto>> CallGoogleAsync(PlaceSearchQuery query, CancellationToken ct)
    {
        var hasText = !string.IsNullOrWhiteSpace(query.Query);
        var endpoint = hasText ? "v1/places:searchText" : "v1/places:searchNearby";

        var circle = new
        {
            center = new { latitude = query.Latitude, longitude = query.Longitude },
            radius = (double)settings.RadiusMeters
        };

        object body = hasText
            ? new
            {
                textQuery = query.Query,
                languageCode = "ar",
                regionCode = "SA",
                maxResultCount = 20,
                locationBias = new { circle }
            }
            : new
            {
                languageCode = "ar",
                regionCode = "SA",
                maxResultCount = 20,
                includedTypes = TypesFor(query.Category),
                locationRestriction = new { circle }
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Goog-Api-Key", settings.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", "places.id,places.displayName,places.location,places.types,places.formattedAddress");

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Google a repondu {(int)response.StatusCode} : {detail}");
        }

        var payload = await response.Content.ReadFromJsonAsync<GoogleResponse>(cancellationToken: ct);
        return Map(payload, query);
    }

    private static string[] TypesFor(string? category) =>
        CategoryTypes.FirstOrDefault(entry => entry.Category == category).Types ?? ["point_of_interest"];

    /// <summary>Traduit la reponse de Google dans la forme de l'application. Visible pour les tests.</summary>
    public static IReadOnlyList<PlaceDto> Map(GoogleResponse? payload, PlaceSearchQuery query)
    {
        if (payload?.Places is null) return [];

        var results = new List<PlaceDto>();
        foreach (var place in payload.Places)
        {
            var name = place.DisplayName?.Text;
            if (string.IsNullOrWhiteSpace(name) || place.Location is null || string.IsNullOrWhiteSpace(place.Id)) continue;

            var category = CategoryOf(place.Types);
            // Une recherche par categorie garde la categorie demandee, meme si les types de
            // Google ne la nomment pas : c'est bien ce que le pelerin cherchait.
            category ??= query.Category;
            if (string.IsNullOrWhiteSpace(category)) continue;

            results.Add(new PlaceDto(
                $"google-{place.Id}",
                query.City,
                category,
                name.Trim(),
                new PlaceCoordinate(place.Location.Latitude, place.Location.Longitude),
                place.FormattedAddress));
        }
        return results;
    }

    public sealed class GoogleResponse
    {
        [JsonPropertyName("places")]
        public List<GooglePlace>? Places { get; set; }
    }

    public sealed class GooglePlace
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("displayName")] public GoogleText? DisplayName { get; set; }
        [JsonPropertyName("location")] public GoogleLocation? Location { get; set; }
        [JsonPropertyName("types")] public List<string>? Types { get; set; }
        [JsonPropertyName("formattedAddress")] public string? FormattedAddress { get; set; }
    }

    public sealed class GoogleText
    {
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    public sealed class GoogleLocation
    {
        [JsonPropertyName("latitude")] public double Latitude { get; set; }
        [JsonPropertyName("longitude")] public double Longitude { get; set; }
    }

    /// <summary>Remet le compteur et le cache a zero. Sert uniquement aux tests.</summary>
    public static void ResetForTests()
    {
        lock (counterLock)
        {
            counterDay = DateOnly.FromDateTime(DateTime.UtcNow);
            callsToday = 0;
        }
        cache.Clear();
    }
}
