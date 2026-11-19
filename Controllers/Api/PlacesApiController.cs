using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    /// <summary>
    /// Recherche de lieux autour du pelerin.
    ///
    /// L'application n'appelle JAMAIS Google directement : elle appelle cette route, et le
    /// serveur appelle Google avec une cle qui ne sort pas d'ici. C'est aussi ici que le plafond
    /// de depense est tenu — une application installee sur mille telephones ne peut pas se
    /// discipliner toute seule.
    ///
    /// Quand la recherche distante n'est pas disponible (pas de cle, plafond atteint, Google
    /// muet), la reponse le DIT au lieu de rendre une liste vide : l'application retombe alors
    /// sur ses lieux embarques et affiche la bonne explication.
    /// </summary>
    [ApiController]
    [Route("api/places")]
    public class PlacesApiController(IPlaceSearchService places, IWalkingRouteService routes) : ControllerBase
    {
        /// <summary>Les villes couvertes, avec le centre servant de repli si le telephone n'a pas sa position.</summary>
        private static readonly Dictionary<string, (double Latitude, double Longitude)> CityCenters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["makkah"] = (21.4225, 39.8262),
            ["madinah"] = (24.4672, 39.6111)
        };

        private static readonly HashSet<string> Categories = new(StringComparer.OrdinalIgnoreCase)
        {
            "hotels", "restaurants", "mosques", "health", "exchange", "transport"
        };

        [HttpGet("search")]
        [EnableRateLimiting("places")]
        public async Task<IActionResult> Search(
            [FromQuery] string? q,
            [FromQuery] string? category,
            [FromQuery] string city = "makkah",
            [FromQuery] double? lat = null,
            [FromQuery] double? lng = null,
            CancellationToken ct = default)
        {
            if (!CityCenters.TryGetValue(city, out var center))
            {
                return BadRequest(new { error = "مدينة غير معروفة" });
            }
            if (!string.IsNullOrWhiteSpace(category) && !Categories.Contains(category))
            {
                return BadRequest(new { error = "تصنيف غير معروف" });
            }

            var query = (q ?? "").Trim();
            if (query.Length > 120) query = query[..120];
            if (query.Length == 0 && string.IsNullOrWhiteSpace(category))
            {
                // Ni mot-cle ni categorie : rien a demander a Google, et un appel de moins.
                return Ok(new { source = "none", remainingToday = (int?)null, places = Array.Empty<object>() });
            }

            // Une position aberrante est ignoree : on retombe sur le centre de la ville plutot
            // que de chercher des hotels au milieu de l'ocean.
            var latitude = lat is >= -90 and <= 90 ? lat.Value : center.Latitude;
            var longitude = lng is >= -180 and <= 180 ? lng.Value : center.Longitude;

            var result = await places.SearchAsync(
                new PlaceSearchQuery(query, string.IsNullOrWhiteSpace(category) ? null : category, city.ToLowerInvariant(), latitude, longitude),
                ct);

            return Ok(new
            {
                source = result.Source switch
                {
                    PlaceSource.Google => "google",
                    PlaceSource.Cache => "cache",
                    PlaceSource.QuotaReached => "quota",
                    PlaceSource.NotConfigured => "not-configured",
                    _ => "unavailable"
                },
                remainingToday = result.RemainingToday,
                places = result.Places.Select(place => new
                {
                    id = place.Id,
                    city = place.City,
                    category = place.Category,
                    name = place.Name,
                    address = place.Address,
                    coordinate = new { latitude = place.Coordinate.Latitude, longitude = place.Coordinate.Longitude }
                })
            });
        }

        /// <summary>
        /// Le trajet a pied entre le pelerin et un lieu. Rend le trace, la distance et la duree —
        /// pas un guidage : l application dessine le chemin sur sa carte, elle n accompagne pas
        /// le pelerin pas a pas et ne recalcule rien s il tourne au mauvais coin.
        /// </summary>
        [HttpGet("route")]
        [EnableRateLimiting("places")]
        public async Task<IActionResult> Route(
            [FromQuery] double fromLat,
            [FromQuery] double fromLng,
            [FromQuery] double toLat,
            [FromQuery] double toLng,
            CancellationToken ct = default)
        {
            static bool Valid(double latitude, double longitude) =>
                latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

            if (!Valid(fromLat, fromLng) || !Valid(toLat, toLng))
            {
                return BadRequest(new { error = "إحداثيات غير صالحة" });
            }

            var result = await routes.RouteAsync(fromLat, fromLng, toLat, toLng, ct);

            return Ok(new
            {
                source = result.Source switch
                {
                    PlaceSource.Google => "google",
                    PlaceSource.Cache => "cache",
                    PlaceSource.QuotaReached => "quota",
                    PlaceSource.NotConfigured => "not-configured",
                    _ => "unavailable"
                },
                remainingToday = result.RemainingToday,
                route = result.Route is null ? null : new
                {
                    distanceMeters = result.Route.DistanceMeters,
                    durationSeconds = result.Route.DurationSeconds,
                    polyline = result.Route.EncodedPolyline
                }
            });
        }
    }
}
