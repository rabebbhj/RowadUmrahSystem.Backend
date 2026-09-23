using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Route("api/package-pricing")]
    public class PackagePricingApiController : ControllerBase
    {
        private static readonly string[] PackageDays = { "10 أيام", "6 أيام" };
        private static readonly string[] Nationalities =
        {
            "هندي",
            "بنغلاديشي",
            "مصري",
            "سوري",
            "سوداني",
            "نيجيري",
            "افغاني",
            "فلسطيني",
            "كويتي",
            "سعودي"
        };

        private static readonly HotelPricingSeed[] Hotels =
        {
            new("فندق ساعة مكة فيرمونت", 1250m),
            new("فندق جبل عمر حياة ريجنسي", 980m),
            new("فندق موفنبيك برج هاجر", 750m)
        };

        private static readonly Dictionary<string, decimal> NationalityFactors = new()
        {
            ["هندي"] = 0.90m,
            ["بنغلاديشي"] = 0.88m,
            ["مصري"] = 0.95m,
            ["سوري"] = 0.93m,
            ["سوداني"] = 0.90m,
            ["نيجيري"] = 0.98m,
            ["افغاني"] = 0.89m,
            ["فلسطيني"] = 0.94m,
            ["كويتي"] = 1.05m,
            ["سعودي"] = 1.00m
        };

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly string _filePath;

        public PackagePricingApiController(IWebHostEnvironment environment)
        {
            _filePath = Path.Combine(environment.ContentRootPath, "App_Data", "package-pricing.json");
        }

        [HttpGet]
        public async Task<ActionResult<PackagePricingDto>> Get()
        {
            return Ok(await ReadOrCreateAsync());
        }

        [Authorize(Roles = "Admin")]
        [HttpPut]
        public async Task<ActionResult<PackagePricingDto>> Update([FromBody] PackagePricingUpdateDto request)
        {
            var normalized = BuildPricing(request.Items ?? Array.Empty<PackagePricingItemDto>());
            await WriteAsync(normalized);

            return Ok(normalized);
        }

        private async Task<PackagePricingDto> ReadOrCreateAsync()
        {
            if (!System.IO.File.Exists(_filePath))
            {
                var defaultPricing = BuildPricing(Array.Empty<PackagePricingItemDto>());
                await WriteAsync(defaultPricing);
                return defaultPricing;
            }

            try
            {
                await using var stream = System.IO.File.OpenRead(_filePath);
                var saved = await JsonSerializer.DeserializeAsync<PackagePricingDto>(stream, JsonOptions);
                return BuildPricing(saved?.Items ?? Array.Empty<PackagePricingItemDto>());
            }
            catch
            {
                var defaultPricing = BuildPricing(Array.Empty<PackagePricingItemDto>());
                await WriteAsync(defaultPricing);
                return defaultPricing;
            }
        }

        private async Task WriteAsync(PackagePricingDto pricing)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using var stream = System.IO.File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, pricing, JsonOptions);
        }

        private static PackagePricingDto BuildPricing(IReadOnlyCollection<PackagePricingItemDto> submittedItems)
        {
            var submitted = submittedItems
                .GroupBy(item => MakeKey(item.PackageDays, item.HotelName, item.Nationality))
                .ToDictionary(group => group.Key, group => Math.Max(0m, group.Last().Price));

            var items = new List<PackagePricingItemDto>();

            foreach (var packageDays in PackageDays)
            {
                foreach (var hotel in Hotels)
                {
                    foreach (var nationality in Nationalities)
                    {
                        var key = MakeKey(packageDays, hotel.Name, nationality);
                        var defaultPrice = Math.Round(hotel.BasePrice * NationalityFactors[nationality], 0);
                        var price = submitted.TryGetValue(key, out var submittedPrice) ? submittedPrice : defaultPrice;

                        items.Add(new PackagePricingItemDto(packageDays, hotel.Name, nationality, price));
                    }
                }
            }

            return new PackagePricingDto(
                Nationalities,
                Hotels.Select(hotel => hotel.Name).ToArray(),
                items);
        }

        private static string MakeKey(string packageDays, string hotelName, string nationality)
        {
            return $"{packageDays.Trim()}|{hotelName.Trim()}|{nationality.Trim()}";
        }

        private sealed record HotelPricingSeed(string Name, decimal BasePrice);
    }

    public sealed record PackagePricingDto(
        IReadOnlyList<string> Nationalities,
        IReadOnlyList<string> Hotels,
        IReadOnlyList<PackagePricingItemDto> Items);

    public sealed record PackagePricingUpdateDto(IReadOnlyList<PackagePricingItemDto>? Items);

    public sealed record PackagePricingItemDto(
        string PackageDays,
        string HotelName,
        string Nationality,
        decimal Price);
}
