using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace RowadUmrahSystem.Web.Services;

/// <summary>
/// Recherche de lieux reels autour du pelerin.
///
/// Deux fournisseurs :
///   - Google Places API si une cle est configuree (plus riche, payant) ;
///   - Nominatim (OpenStreetMap) sinon, ou quand Google echoue (gratuit, sans cle).
///
/// Le choix est automatique : Google en premier quand la cle est la, Nominatim en fallback.
/// Le format de sortie est le meme : l'application ne voit aucune difference.
/// </summary>
public sealed class PlaceSearchOptions
{
    public string ApiKey { get; set; } = "";
    public int DailyRequestLimit { get; set; } = 300;
    public int RouteDailyRequestLimit { get; set; } = 200;
    public int CacheHours { get; set; } = 12;
    public int RadiusMeters { get; set; } = 3000;
}

public sealed record PlaceDto(
    string Id,
    string City,
    string Category,
    string Name,
    PlaceCoordinate Coordinate,
    string? Address);

public sealed record PlaceCoordinate(double Latitude, double Longitude);

public enum PlaceSource
{
    Google,
    OpenStreetMap,
    Cache,
    NotConfigured,
    QuotaReached,
    Unavailable
}

public sealed record PlaceSearchResult(PlaceSource Source, IReadOnlyList<PlaceDto> Places, int RemainingToday);

public sealed record PlaceSearchQuery(string Query, string? Category, string City, double Latitude, double Longitude);

public interface IPlaceSearchService
{
    bool IsConfigured { get; }
    Task<PlaceSearchResult> SearchAsync(PlaceSearchQuery query, CancellationToken ct);
}

public sealed class PlaceSearchService : IPlaceSearchService
{
    private readonly HttpClient _google;
    private readonly HttpClient _nominatim;
    private readonly HttpClient _photon;
    private readonly PlaceSearchOptions _settings;
    private readonly ILogger<PlaceSearchService> _log;

    public PlaceSearchService(
        IHttpClientFactory httpFactory,
        IOptions<PlaceSearchOptions> options,
        ILogger<PlaceSearchService> log)
    {
        _google = httpFactory.CreateClient("GooglePlaces");
        _nominatim = httpFactory.CreateClient("Nominatim");
        _photon = httpFactory.CreateClient("Photon");
        _settings = options.Value;
        _log = log;
    }

    public bool IsConfigured => true;

    private bool GoogleConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    private static readonly object counterLock = new();
    private static DateOnly counterDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private static int callsToday;

    private static readonly ConcurrentDictionary<string, (DateTimeOffset Expires, IReadOnlyList<PlaceDto> Places)> cache = new();

    private static readonly (string Category, string[] GoogleTypes, string[] OsmTags)[] CategoryTypes =
    [
        ("hotels", ["lodging", "hotel", "motel", "guest_house", "resort_hotel", "extended_stay_hotel"],
            ["tourism=hotel", "tourism=guest_house", "tourism=hostel", "tourism=motel"]),
        ("restaurants", ["restaurant", "cafe", "bakery", "meal_takeaway", "meal_delivery", "coffee_shop", "fast_food_restaurant"],
            ["amenity=restaurant", "amenity=cafe", "amenity=fast_food", "amenity=food_court"]),
        ("mosques", ["mosque", "place_of_worship"],
            ["amenity=place_of_worship", "building=mosque"]),
        ("health", ["pharmacy", "drugstore", "hospital", "doctor", "medical_lab", "dental_clinic"],
            ["amenity=pharmacy", "amenity=hospital", "amenity=clinic", "amenity=doctors"]),
        ("exchange", ["bank", "atm", "money_transfer", "currency_exchange", "accounting"],
            ["amenity=bank", "amenity=atm", "amenity=bureau_de_change"]),
        ("transport", ["bus_station", "bus_stop", "transit_station", "taxi_stand", "parking", "train_station", "airport"],
            ["amenity=bus_station", "public_transport=station", "amenity=taxi", "railway=station"])
    ];

    private static string? CategoryOf(IEnumerable<string>? types)
    {
        if (types is null) return null;
        var set = types.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, googleTypes, _) in CategoryTypes)
        {
            if (googleTypes.Any(set.Contains)) return category;
        }
        return null;
    }

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
            if (callsToday >= _settings.DailyRequestLimit)
            {
                remaining = 0;
                return false;
            }
            callsToday++;
            remaining = _settings.DailyRequestLimit - callsToday;
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

    private static string CacheKey(PlaceSearchQuery query) => string.Create(
        CultureInfo.InvariantCulture,
        $"{query.Query.Trim().ToLowerInvariant()}|{query.Category}|{query.City}|{Math.Round(query.Latitude, 2)}|{Math.Round(query.Longitude, 2)}");

    public async Task<PlaceSearchResult> SearchAsync(PlaceSearchQuery query, CancellationToken ct)
    {
        var key = CacheKey(query);
        if (cache.TryGetValue(key, out var hit) && hit.Expires > DateTimeOffset.UtcNow)
        {
            return new PlaceSearchResult(PlaceSource.Cache, hit.Places, RemainingNow(_settings.DailyRequestLimit));
        }

        if (GoogleConfigured)
        {
            var google = await SearchViaGoogleAsync(key, query, ct);
            if (google.Places.Count > 0) return google;
            _log.LogInformation("Google indisponible pour la recherche, bascule vers Nominatim.");
        }

        return await SearchViaNominatimAsync(key, query, ct);
    }

    // ── Google Places API ──────────────────────────────────────────────

    private async Task<PlaceSearchResult> SearchViaGoogleAsync(string key, PlaceSearchQuery query, CancellationToken ct)
    {
        if (!TryReserveCall(out var remaining))
        {
            _log.LogWarning("Recherche de lieux : plafond quotidien de {Limit} appels atteint.", _settings.DailyRequestLimit);
            return new PlaceSearchResult(PlaceSource.QuotaReached, [], 0);
        }

        try
        {
            var places = await CallGoogleAsync(query, ct);
            cache[key] = (DateTimeOffset.UtcNow.AddHours(_settings.CacheHours), places);
            return new PlaceSearchResult(PlaceSource.Google, places, remaining);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _log.LogError(error, "Recherche de lieux : appel a Google en echec.");
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
            radius = (double)_settings.RadiusMeters
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
                includedTypes = GoogleTypesFor(query.Category),
                locationRestriction = new { circle }
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Goog-Api-Key", _settings.ApiKey);
        request.Headers.Add("X-Goog-FieldMask", "places.id,places.displayName,places.location,places.types,places.formattedAddress");

        using var response = await _google.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Google a repondu {(int)response.StatusCode} : {detail}");
        }

        var payload = await response.Content.ReadFromJsonAsync<GoogleResponse>(cancellationToken: ct);
        return MapGoogle(payload, query);
    }

    private static string[] GoogleTypesFor(string? category) =>
        CategoryTypes.FirstOrDefault(entry => entry.Category == category).GoogleTypes ?? ["point_of_interest"];

    public static IReadOnlyList<PlaceDto> MapGoogle(GoogleResponse? payload, PlaceSearchQuery query)
    {
        if (payload?.Places is null) return [];

        var results = new List<PlaceDto>();
        foreach (var place in payload.Places)
        {
            var name = place.DisplayName?.Text;
            if (string.IsNullOrWhiteSpace(name) || place.Location is null || string.IsNullOrWhiteSpace(place.Id)) continue;

            var category = CategoryOf(place.Types);
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

    // ── Nominatim (OSM, gratuit) ───────────────────────────────────────

    private async Task<PlaceSearchResult> SearchViaNominatimAsync(string key, PlaceSearchQuery query, CancellationToken ct)
    {
        try
        {
            var places = await CallNominatimAsync(query, ct);
            if (places.Count > 0)
            {
                cache[key] = (DateTimeOffset.UtcNow.AddHours(_settings.CacheHours), places);
                return new PlaceSearchResult(PlaceSource.Google, places, 0);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _log.LogInformation(error, "Nominatim injoignable, bascule vers Photon.");
        }

        return await SearchViaPhotonAsync(key, query, ct);
    }

    private async Task<IReadOnlyList<PlaceDto>> CallNominatimAsync(PlaceSearchQuery query, CancellationToken ct)
    {
        var hasText = !string.IsNullOrWhiteSpace(query.Query);

        var viewbox = string.Create(CultureInfo.InvariantCulture,
            $"{query.Longitude - 0.03},{query.Latitude + 0.03},{query.Longitude + 0.03},{query.Latitude - 0.03}");

        string url;
        if (hasText)
        {
            var q = Uri.EscapeDataString(query.Query);
            url = $"search?q={q}&format=jsonv2&limit=20&accept-language=ar&viewbox={viewbox}&bounded=0&addressdetails=1";
        }
        else
        {
            var osmTags = OsmTagsFor(query.Category);
            if (osmTags.Length == 0) return [];
            var tagQuery = Uri.EscapeDataString(string.Join(",", osmTags.Select(t => $"[{t.Replace('=', '~')}]")));
            url = $"search?q={tagQuery}&format=jsonv2&limit=20&accept-language=ar&viewbox={viewbox}&bounded=1&addressdetails=1";
        }

        using var response = await _nominatim.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];

        var items = await response.Content.ReadFromJsonAsync<List<NominatimPlace>>(cancellationToken: ct);
        return MapNominatim(items, query);
    }

    private static string[] OsmTagsFor(string? category) =>
        CategoryTypes.FirstOrDefault(entry => entry.Category == category).OsmTags ?? [];

    public static IReadOnlyList<PlaceDto> MapNominatim(List<NominatimPlace>? items, PlaceSearchQuery query)
    {
        if (items is null || items.Count == 0) return [];

        var results = new List<PlaceDto>();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.DisplayName)) continue;
            if (!double.TryParse(item.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(item.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) continue;

            var name = item.Name ?? item.DisplayName.Split(',')[0].Trim();
            var category = NominatimCategoryOf(item.Category, item.Type) ?? query.Category;
            if (string.IsNullOrWhiteSpace(category)) category = "hotels";

            results.Add(new PlaceDto(
                $"osm-{item.OsmType?[0]}{item.OsmId}",
                query.City,
                category,
                name,
                new PlaceCoordinate(lat, lon),
                item.DisplayName));
        }
        return results;
    }

    private static string? NominatimCategoryOf(string? osmCategory, string? osmType)
    {
        if (osmCategory is null && osmType is null) return null;
        var cat = osmCategory?.ToLowerInvariant() ?? "";
        var typ = osmType?.ToLowerInvariant() ?? "";

        if (typ is "hotel" or "guest_house" or "hostel" or "motel" || cat == "tourism") return "hotels";
        if (typ is "restaurant" or "cafe" or "fast_food" or "food_court" || cat == "restaurant") return "restaurants";
        if (typ is "mosque" or "place_of_worship") return "mosques";
        if (typ is "pharmacy" or "hospital" or "clinic" or "doctors" || cat == "healthcare") return "health";
        if (typ is "bank" or "atm" or "bureau_de_change") return "exchange";
        if (typ is "bus_station" or "taxi" or "station" || cat is "public_transport" or "railway") return "transport";
        return null;
    }

    // ── Photon (komoot, donnees OSM, gratuit) ───────────────────────────

    private async Task<PlaceSearchResult> SearchViaPhotonAsync(string key, PlaceSearchQuery query, CancellationToken ct)
    {
        try
        {
            var places = await CallPhotonAsync(query, ct);
            if (places.Count > 0)
            {
                cache[key] = (DateTimeOffset.UtcNow.AddHours(_settings.CacheHours), places);
            }
            return new PlaceSearchResult(PlaceSource.Google, places, 0);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            _log.LogError(error, "Recherche de lieux : appel a Photon en echec.");
            return new PlaceSearchResult(PlaceSource.Unavailable, [], 0);
        }
    }

    private async Task<IReadOnlyList<PlaceDto>> CallPhotonAsync(PlaceSearchQuery query, CancellationToken ct)
    {
        var hasText = !string.IsNullOrWhiteSpace(query.Query);
        var searchTerm = hasText ? query.Query : PhotonTermFor(query.Category);
        if (string.IsNullOrWhiteSpace(searchTerm)) return [];

        var q = Uri.EscapeDataString(searchTerm);
        var url = string.Create(CultureInfo.InvariantCulture,
            $"api/?q={q}&lat={query.Latitude}&lon={query.Longitude}&limit=20&lang=en");

        using var response = await _photon.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];

        var payload = await response.Content.ReadFromJsonAsync<PhotonResponse>(cancellationToken: ct);
        return MapPhoton(payload, query);
    }

    private static string? PhotonTermFor(string? category) => category switch
    {
        "hotels" => "hotel",
        "restaurants" => "restaurant cafe",
        "mosques" => "mosque",
        "health" => "pharmacy hospital",
        "exchange" => "bank atm",
        "transport" => "bus station taxi",
        _ => null
    };

    public static IReadOnlyList<PlaceDto> MapPhoton(PhotonResponse? payload, PlaceSearchQuery query)
    {
        if (payload?.Features is null || payload.Features.Count == 0) return [];

        var results = new List<PlaceDto>();
        foreach (var feature in payload.Features)
        {
            var props = feature.Properties;
            var geom = feature.Geometry;
            if (props is null || geom?.Coordinates is null || geom.Coordinates.Count < 2) continue;

            var name = props.Name;
            if (string.IsNullOrWhiteSpace(name)) name = props.Street ?? props.City;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var lon = geom.Coordinates[0];
            var lat = geom.Coordinates[1];

            var category = PhotonCategoryOf(props.OsmKey, props.OsmValue) ?? query.Category;
            if (string.IsNullOrWhiteSpace(category)) category = "hotels";

            var address = string.Join(", ", new[] { props.Street, props.City, props.Country }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

            results.Add(new PlaceDto(
                $"osm-{props.OsmType?[0]}{props.OsmId}",
                query.City,
                category,
                name,
                new PlaceCoordinate(lat, lon),
                string.IsNullOrWhiteSpace(address) ? null : address));
        }
        return results;
    }

    private static string? PhotonCategoryOf(string? osmKey, string? osmValue)
    {
        var key = osmKey?.ToLowerInvariant() ?? "";
        var val = osmValue?.ToLowerInvariant() ?? "";

        if (val is "hotel" or "guest_house" or "hostel" or "motel" || key == "tourism") return "hotels";
        if (val is "restaurant" or "cafe" or "fast_food" or "food_court") return "restaurants";
        if (val is "mosque" or "place_of_worship") return "mosques";
        if (val is "pharmacy" or "hospital" or "clinic" or "doctors" || key == "healthcare") return "health";
        if (val is "bank" or "atm" or "bureau_de_change") return "exchange";
        if (val is "bus_station" or "taxi" or "station" || key is "public_transport" or "railway") return "transport";
        return null;
    }

    // ── Modeles de reponse ─────────────────────────────────────────────

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

    public sealed class NominatimPlace
    {
        [JsonPropertyName("osm_type")] public string? OsmType { get; set; }
        [JsonPropertyName("osm_id")] public long OsmId { get; set; }
        [JsonPropertyName("lat")] public string? Lat { get; set; }
        [JsonPropertyName("lon")] public string? Lon { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }

    public sealed class PhotonResponse
    {
        [JsonPropertyName("features")] public List<PhotonFeature>? Features { get; set; }
    }

    public sealed class PhotonFeature
    {
        [JsonPropertyName("properties")] public PhotonProperties? Properties { get; set; }
        [JsonPropertyName("geometry")] public PhotonGeometry? Geometry { get; set; }
    }

    public sealed class PhotonProperties
    {
        [JsonPropertyName("osm_type")] public string? OsmType { get; set; }
        [JsonPropertyName("osm_id")] public long OsmId { get; set; }
        [JsonPropertyName("osm_key")] public string? OsmKey { get; set; }
        [JsonPropertyName("osm_value")] public string? OsmValue { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("street")] public string? Street { get; set; }
        [JsonPropertyName("city")] public string? City { get; set; }
        [JsonPropertyName("country")] public string? Country { get; set; }
    }

    public sealed class PhotonGeometry
    {
        [JsonPropertyName("coordinates")] public List<double>? Coordinates { get; set; }
    }

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
