using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Route("api/packages")]
    public class PackagesApiController : ControllerBase
    {
        private const string PublishedStatus = "published";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private readonly string _filePath;

        public PackagesApiController(IWebHostEnvironment environment)
        {
            _filePath = Path.Combine(environment.ContentRootPath, "App_Data", "travel-packages.json");
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<TravelPackageDto>>> GetPublished()
        {
            var packages = await ReadOrCreateAsync();

            return Ok(packages
                .Where(package => package.Status == PublishedStatus)
                .OrderBy(package => package.DisplayOrder)
                .ThenBy(package => package.CreatedAt)
                .ToList());
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("admin")]
        public async Task<ActionResult<IReadOnlyList<TravelPackageDto>>> GetAdmin()
        {
            var packages = await ReadOrCreateAsync();

            return Ok(packages
                .OrderBy(package => package.DisplayOrder)
                .ThenBy(package => package.CreatedAt)
                .ToList());
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("{id}")]
        public async Task<ActionResult<TravelPackageDto>> GetById(string id)
        {
            var packages = await ReadOrCreateAsync();
            var package = packages.FirstOrDefault(item => item.Id == id);

            return package == null ? NotFound() : Ok(package);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<ActionResult<TravelPackageDto>> Create([FromBody] TravelPackageDto request)
        {
            var packages = await ReadOrCreateAsync();
            var package = NormalizePackage(request with
            {
                Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            packages.Add(package);
            await WriteAsync(packages);

            return CreatedAtAction(nameof(GetById), new { id = package.Id }, package);
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<ActionResult<TravelPackageDto>> Update(string id, [FromBody] TravelPackageDto request)
        {
            var packages = await ReadOrCreateAsync();
            var index = packages.FindIndex(package => package.Id == id);

            if (index < 0)
            {
                return NotFound();
            }

            var current = packages[index];
            var package = NormalizePackage(request with
            {
                Id = id,
                CreatedAt = current.CreatedAt,
                UpdatedAt = DateTime.UtcNow
            });

            packages[index] = package;
            await WriteAsync(packages);

            return Ok(package);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/duplicate")]
        public async Task<ActionResult<TravelPackageDto>> Duplicate(string id)
        {
            var packages = await ReadOrCreateAsync();
            var package = packages.FirstOrDefault(item => item.Id == id);

            if (package == null)
            {
                return NotFound();
            }

            var copy = NormalizePackage(package with
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = $"{package.Name} - نسخة",
                Status = "draft",
                DisplayOrder = packages.Count == 0 ? 1 : packages.Max(item => item.DisplayOrder) + 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            packages.Add(copy);
            await WriteAsync(packages);

            return Ok(copy);
        }

        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var packages = await ReadOrCreateAsync();
            var removed = packages.RemoveAll(package => package.Id == id);

            if (removed == 0)
            {
                return NotFound();
            }

            await WriteAsync(packages);
            return NoContent();
        }

        private async Task<List<TravelPackageDto>> ReadOrCreateAsync()
        {
            if (!System.IO.File.Exists(_filePath))
            {
                var defaults = BuildDefaultPackages();
                await WriteAsync(defaults);
                return defaults;
            }

            try
            {
                await using var stream = System.IO.File.OpenRead(_filePath);
                var packages = await JsonSerializer.DeserializeAsync<List<TravelPackageDto>>(stream, JsonOptions);

                return (packages ?? BuildDefaultPackages())
                    .Select(NormalizePackage)
                    .OrderBy(package => package.DisplayOrder)
                    .ThenBy(package => package.CreatedAt)
                    .ToList();
            }
            catch
            {
                var defaults = BuildDefaultPackages();
                await WriteAsync(defaults);
                return defaults;
            }
        }

        private async Task WriteAsync(IReadOnlyCollection<TravelPackageDto> packages)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            await using var stream = System.IO.File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, packages, JsonOptions);
        }

        private static TravelPackageDto NormalizePackage(TravelPackageDto package)
        {
            var id = string.IsNullOrWhiteSpace(package.Id) ? Guid.NewGuid().ToString("N") : package.Id;
            var durationDays = package.DurationDays <= 0 ? 6 : package.DurationDays;
            var durationLabel = string.IsNullOrWhiteSpace(package.DurationLabel) ? $"{durationDays} أيام" : package.DurationLabel.Trim();
            var status = string.IsNullOrWhiteSpace(package.Status) ? "draft" : package.Status.Trim();
            var priceMode = package.PriceMode == "rules" ? "rules" : "fixed";

            return package with
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(package.Name) ? durationLabel : package.Name.Trim(),
                ShortTitle = string.IsNullOrWhiteSpace(package.ShortTitle) ? "رحلة عمرة" : package.ShortTitle.Trim(),
                Description = package.Description?.Trim() ?? "",
                DurationDays = durationDays,
                DurationLabel = durationLabel,
                ImageUrl = string.IsNullOrWhiteSpace(package.ImageUrl) ? "/landingpage/avion.png" : package.ImageUrl.Trim(),
                BasePrice = Math.Max(0, package.BasePrice),
                VisaSupplement = Math.Max(0, package.VisaSupplement),
                Currency = string.IsNullOrWhiteSpace(package.Currency) ? "د.ك" : package.Currency.Trim(),
                PriceMode = priceMode,
                Status = status,
                DisplayOrder = package.DisplayOrder <= 0 ? 1 : package.DisplayOrder,
                TransportOptions = NormalizeOptions(package.TransportOptions),
                RoomTypes = NormalizeOptions(package.RoomTypes),
                DepartureDates = package.DepartureDates
                    .Where(date => !string.IsNullOrWhiteSpace(date))
                    .Select(date => date.Trim())
                    .Distinct()
                    .OrderBy(date => date)
                    .ToList(),
                PricingRules = NormalizeRules(package.PricingRules),
                CreatedAt = package.CreatedAt == default ? DateTime.UtcNow : package.CreatedAt,
                UpdatedAt = package.UpdatedAt == default ? DateTime.UtcNow : package.UpdatedAt
            };
        }

        private static List<PackageOptionDto> NormalizeOptions(IReadOnlyList<PackageOptionDto> options)
        {
            return options
                .Where(option => !string.IsNullOrWhiteSpace(option.Label))
                .Select(option => option with
                {
                    Id = string.IsNullOrWhiteSpace(option.Id) ? Guid.NewGuid().ToString("N") : option.Id,
                    Label = option.Label.Trim(),
                    Active = option.Active,
                    Supplement = Math.Max(0, option.Supplement ?? 0),
                    Price = option.Price.HasValue ? Math.Max(0, option.Price.Value) : null
                })
                .ToList();
        }

        private static List<PricingRuleDto> NormalizeRules(IReadOnlyList<PricingRuleDto> rules)
        {
            return rules
                .Where(rule => !string.IsNullOrWhiteSpace(rule.Name))
                .Select(rule => rule with
                {
                    Id = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id,
                    Name = rule.Name.Trim(),
                    Price = Math.Max(0, rule.Price),
                    PriceType = string.IsNullOrWhiteSpace(rule.PriceType) ? "perPerson" : rule.PriceType.Trim(),
                    Priority = Math.Max(0, rule.Priority),
                    Conditions = rule.Conditions
                        .Where(condition => !string.IsNullOrWhiteSpace(condition.Field) && !string.IsNullOrWhiteSpace(condition.Operator))
                        .Select(condition => condition with
                        {
                            Field = condition.Field.Trim(),
                            Operator = condition.Operator.Trim(),
                            Value = condition.Value?.Trim() ?? ""
                        })
                        .ToList()
                })
                .ToList();
        }

        private static List<TravelPackageDto> BuildDefaultPackages()
        {
            var now = DateTime.UtcNow;

            return new List<TravelPackageDto>
            {
                NormalizePackage(new TravelPackageDto(
                    "umrah-10-days",
                    "باقة العمرة 10 أيام",
                    "رحلة متكاملة لا تنسى",
                    "باقة عمرة تشمل الإقامة والتنقلات وخدمة المتابعة.",
                    10,
                    "10 أيام",
                    "/landingpage/avion.png",
                    95m,
                    0m,
                    "د.ك",
                    "rules",
                    PublishedStatus,
                    1,
                    new List<PackageOptionDto>
                    {
                        new("bus", "باص", true, 0m, null),
                        new("private-car", "سيارة فردية", true, 500m, null)
                    },
                    new List<PackageOptionDto>
                    {
                        new("double", "غرفة مزدوجة", true, 0m, null),
                        new("triple", "غرفة ثلاثية", true, 15m, null),
                        new("quad", "غرفة رباعية", true, 0m, null),
                        new("single", "غرفة فردية", true, 60m, null)
                    },
                    new List<string> { "2026-10-07", "2026-10-14", "2026-10-21" },
                    new List<PricingRuleDto>
                    {
                        new("rule-10-default", "السعر الأساسي - 10 أيام", Array.Empty<PricingConditionDto>(), 95m, "perPerson", 10, true),
                        new("rule-10-double", "غرفة ثنائية - باص", new List<PricingConditionDto>
                        {
                            new("roomType", "equals", "غرفة مزدوجة"),
                            new("transport", "equals", "باص")
                        }, 95m, "perPerson", 80, true)
                    },
                    now,
                    now)),
                NormalizePackage(new TravelPackageDto(
                    "umrah-6-days",
                    "باقة العمرة 6 أيام",
                    "رحلة اقتصادية مريحة",
                    "باقة قصيرة مناسبة للحجوزات السريعة.",
                    6,
                    "6 أيام",
                    "/landingpage/paysage.png",
                    75m,
                    0m,
                    "د.ك",
                    "rules",
                    PublishedStatus,
                    2,
                    new List<PackageOptionDto>
                    {
                        new("bus", "باص", true, 0m, null),
                        new("private-car", "سيارة فردية", true, 350m, null)
                    },
                    new List<PackageOptionDto>
                    {
                        new("double", "غرفة مزدوجة", true, 0m, null),
                        new("triple", "غرفة ثلاثية", true, 10m, null),
                        new("quad", "غرفة رباعية", true, 0m, null)
                    },
                    new List<string> { "2026-10-01", "2026-10-08", "2026-10-15" },
                    new List<PricingRuleDto>
                    {
                        new("rule-6-default", "السعر الأساسي - 6 أيام", Array.Empty<PricingConditionDto>(), 75m, "perPerson", 10, true),
                        new("rule-6-double", "غرفة ثنائية - باص", new List<PricingConditionDto>
                        {
                            new("roomType", "equals", "غرفة مزدوجة"),
                            new("transport", "equals", "باص")
                        }, 75m, "perPerson", 80, true)
                    },
                    now,
                    now))
            };
        }
    }

    public sealed record TravelPackageDto(
        string Id,
        string Name,
        string ShortTitle,
        string Description,
        int DurationDays,
        string DurationLabel,
        string ImageUrl,
        decimal BasePrice,
        decimal VisaSupplement,
        string Currency,
        string PriceMode,
        string Status,
        int DisplayOrder,
        IReadOnlyList<PackageOptionDto> TransportOptions,
        IReadOnlyList<PackageOptionDto> RoomTypes,
        IReadOnlyList<string> DepartureDates,
        IReadOnlyList<PricingRuleDto> PricingRules,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public sealed record PackageOptionDto(
        string Id,
        string Label,
        bool Active,
        decimal? Supplement,
        decimal? Price);

    public sealed record PricingRuleDto(
        string Id,
        string Name,
        IReadOnlyList<PricingConditionDto> Conditions,
        decimal Price,
        string PriceType,
        int Priority,
        bool Active);

    public sealed record PricingConditionDto(
        string Field,
        string Operator,
        string Value);
}
