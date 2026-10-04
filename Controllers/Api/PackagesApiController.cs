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
                Name = $"{package.Name} - Ù†Ø³Ø®Ø©",
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
                var defaults = MergeFlexibleUmrahPackages(BuildDefaultPackages());
                await WriteAsync(defaults);
                return defaults;
            }

            try
            {
                await using var stream = System.IO.File.OpenRead(_filePath);
                var packages = await JsonSerializer.DeserializeAsync<List<TravelPackageDto>>(stream, JsonOptions);

                var normalizedPackages = (packages ?? BuildDefaultPackages())
                    .Select(NormalizePackage)
                    .ToList();
                var shouldWriteMergedPackages = normalizedPackages.Any(package =>
                    package.Id == LegacyUmrah10PackageId ||
                    package.Id == LegacyUmrah6PackageId ||
                    package.Id == FlexibleUmrahPackageId && NeedsFlexiblePricingProfileUpdate(package));
                var mergedPackages = MergeFlexibleUmrahPackages(normalizedPackages);

                if (shouldWriteMergedPackages ||
                    mergedPackages.Count != normalizedPackages.Count ||
                    mergedPackages.Any(package => package.Id == FlexibleUmrahPackageId) &&
                    normalizedPackages.All(package => package.Id != FlexibleUmrahPackageId))
                {
                    await WriteAsync(mergedPackages);
                }

                return mergedPackages
                    .OrderBy(package => package.DisplayOrder)
                    .ThenBy(package => package.CreatedAt)
                    .ToList();
            }
            catch
            {
                var defaults = MergeFlexibleUmrahPackages(BuildDefaultPackages());
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
            var durationLabel = string.IsNullOrWhiteSpace(package.DurationLabel) ? $"{durationDays} Ø£ÙŠØ§Ù…" : package.DurationLabel.Trim();
            var status = string.IsNullOrWhiteSpace(package.Status) ? "draft" : package.Status.Trim();
            var priceMode = package.PriceMode == "rules" ? "rules" : "fixed";

            return package with
            {
                Id = id,
                Name = string.IsNullOrWhiteSpace(package.Name) ? durationLabel : package.Name.Trim(),
                ShortTitle = string.IsNullOrWhiteSpace(package.ShortTitle) ? "Ø±Ø­Ù„Ø© Ø¹Ù…Ø±Ø©" : package.ShortTitle.Trim(),
                Description = package.Description?.Trim() ?? "",
                DurationDays = durationDays,
                DurationLabel = durationLabel,
                ImageUrl = string.IsNullOrWhiteSpace(package.ImageUrl) ? "/landingpage/avion.png" : package.ImageUrl.Trim(),
                BasePrice = Math.Max(0, package.BasePrice),
                VisaSupplement = Math.Max(0, package.VisaSupplement),
                Currency = string.IsNullOrWhiteSpace(package.Currency) ? "Ø¯.Ùƒ" : package.Currency.Trim(),
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
                PricingProfile = package.PricingProfile,
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

        private const string FlexibleUmrahPackageId = "umrah-flexible";
        private const string LegacyUmrah10PackageId = "umrah-10-days";
        private const string LegacyUmrah6PackageId = "umrah-6-days";

        private static List<TravelPackageDto> MergeFlexibleUmrahPackages(List<TravelPackageDto> packages)
        {
            var existingFlexible = packages.FirstOrDefault(package => package.Id == FlexibleUmrahPackageId);
            var legacy10 = packages.FirstOrDefault(package => package.Id == LegacyUmrah10PackageId);
            var legacy6 = packages.FirstOrDefault(package => package.Id == LegacyUmrah6PackageId);

            if (legacy10 == null && legacy6 == null)
            {
                return packages
                    .Select(package => package.Id == FlexibleUmrahPackageId ? EnsureFlexibleUmrahPackage(package) : package)
                    .ToList();
            }

            if (existingFlexible != null)
            {
                return packages
                    .Where(package => package.Id != LegacyUmrah10PackageId && package.Id != LegacyUmrah6PackageId)
                    .Select(package => package.Id == FlexibleUmrahPackageId ? EnsureFlexibleUmrahPackage(package) : package)
                    .ToList();
            }

            var shortPackage = legacy6 ?? legacy10!;
            var longPackage = legacy10 ?? legacy6!;
            var createdAt = new[] { shortPackage.CreatedAt, longPackage.CreatedAt }.Min();
            var updatedAt = new[] { shortPackage.UpdatedAt, longPackage.UpdatedAt, DateTime.UtcNow }.Max();
            var displayOrder = Math.Min(shortPackage.DisplayOrder, longPackage.DisplayOrder);
            var status = shortPackage.Status == PublishedStatus || longPackage.Status == PublishedStatus ? PublishedStatus : shortPackage.Status;
            var basePrice = Math.Min(shortPackage.BasePrice, longPackage.BasePrice);

            var merged = NormalizePackage(shortPackage with
            {
                Id = FlexibleUmrahPackageId,
                Name = "Ø¨Ø§Ù‚Ø© Ø§Ù„Ø¹Ù…Ø±Ø© Ø§Ù„Ù…Ø±Ù†Ø©",
                ShortTitle = "Ø§Ø®ØªØ± Ø±Ø­Ù„ØªÙƒ",
                Description = "Ø¨Ø§Ù‚Ø© Ø¹Ù…Ø±Ø© Ù…Ø±Ù†Ø© ÙŠØ®ØªØ§Ø± ÙÙŠÙ‡Ø§ Ø§Ù„Ù…Ø³Ø§ÙØ± Ø¨Ø¯Ø§ÙŠØ© ÙˆÙ†Ù‡Ø§ÙŠØ© Ø§Ù„Ø±Ø­Ù„Ø©.",
                DurationDays = 6,
                DurationLabel = "6 Ø£ÙŠØ§Ù… ÙØ£ÙƒØ«Ø±",
                ImageUrl = string.IsNullOrWhiteSpace(longPackage.ImageUrl) ? shortPackage.ImageUrl : longPackage.ImageUrl,
                BasePrice = basePrice,
                VisaSupplement = Math.Max(shortPackage.VisaSupplement, longPackage.VisaSupplement),
                PriceMode = "rules",
                Status = status,
                DisplayOrder = displayOrder,
                TransportOptions = EnsurePrivateCarOnly(MergeOptions(shortPackage.TransportOptions, longPackage.TransportOptions)),
                RoomTypes = MergeOptions(shortPackage.RoomTypes, longPackage.RoomTypes),
                DepartureDates = Array.Empty<string>(),
                PricingRules = BuildFlexibleUmrahRules(shortPackage, longPackage),
                PricingProfile = BuildFlexiblePricingProfile(),
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            });

            return packages
                .Where(package => package.Id != LegacyUmrah10PackageId && package.Id != LegacyUmrah6PackageId)
                .Append(merged)
                .ToList();
        }

        private static bool NeedsFlexiblePricingProfileUpdate(TravelPackageDto package)
        {
            return package.PricingProfile == null ||
                package.PricingProfile.VisaPrice != 20m ||
                !package.PricingProfile.BusPrices.TryGetValue("6", out var sixDaysBusPrice) ||
                sixDaysBusPrice != 0m ||
                !package.PricingProfile.BusPrices.TryGetValue("10", out var tenDaysBusPrice) ||
                tenDaysBusPrice != 0m;
        }

        private static TravelPackageDto EnsureFlexibleUmrahPackage(TravelPackageDto package)
        {
            return NormalizePackage(package with
            {
                PriceMode = "rules",
                TransportOptions = EnsurePrivateCarOnly(package.TransportOptions),
                DepartureDates = Array.Empty<string>(),
                PricingRules = BuildFlexibleUmrahRules(package, package),
                PricingProfile = BuildFlexiblePricingProfile()
            });
        }

        private static IReadOnlyList<PackageOptionDto> EnsurePrivateCarOnly(IReadOnlyList<PackageOptionDto> options)
        {
            var normalizedOptions = NormalizeOptions(options);
            var privateCarOption = normalizedOptions.FirstOrDefault(option => option.Id == "private-car");

            return new List<PackageOptionDto>
            {
                privateCarOption == null
                    ? new("private-car", "سيارة خاصة", true, 0m, null)
                    : privateCarOption with { Active = true, Supplement = 0m, Price = null }
            };
        }

        private static RowadPricingProfileDto BuildFlexiblePricingProfile()
        {
            return new RowadPricingProfileDto(
                true,
                20m,
                new Dictionary<string, decimal>
                {
                    ["6"] = 0m,
                    ["10"] = 0m
                },
                new Dictionary<string, IReadOnlyDictionary<string, decimal>>
                {
                    ["6"] = new Dictionary<string, decimal>
                    {
                        ["غرفة رباعية"] = 25m,
                        ["غرفة ثلاثية"] = 30m,
                        ["غرفة مزدوجة"] = 35m,
                        ["غرفة فردية"] = 40m
                    },
                    ["10"] = new Dictionary<string, decimal>
                    {
                        ["غرفة رباعية"] = 35m,
                        ["غرفة ثلاثية"] = 40m,
                        ["غرفة مزدوجة"] = 45m,
                        ["غرفة فردية"] = 50m
                    }
                },
                new Dictionary<string, IReadOnlyDictionary<string, decimal>>
                {
                    ["6"] = new Dictionary<string, decimal>
                    {
                        ["غرفة رباعية"] = 0m,
                        ["غرفة ثلاثية"] = 0m,
                        ["غرفة مزدوجة"] = 0m,
                        ["غرفة فردية"] = 0m
                    },
                    ["10"] = new Dictionary<string, decimal>
                    {
                        ["غرفة رباعية"] = 55m,
                        ["غرفة ثلاثية"] = 60m,
                        ["غرفة مزدوجة"] = 65m,
                        ["غرفة فردية"] = 70m
                    }
                });
        }

        private static List<PackageOptionDto> MergeOptions(params IReadOnlyList<PackageOptionDto>[] optionGroups)
        {
            return optionGroups
                .SelectMany(options => options)
                .Where(option => !string.IsNullOrWhiteSpace(option.Label))
                .GroupBy(option => option.Id)
                .Select(group => group.First())
                .ToList();
        }

        private static IReadOnlyList<PricingRuleDto> BuildFlexibleUmrahRules(TravelPackageDto shortPackage, TravelPackageDto longPackage)
        {
            return new List<PricingRuleDto>
            {
                new(
                    "rule-flex-6-default",
                    "Ø§Ù„Ø³Ø¹Ø± Ø§Ù„Ø£Ø³Ø§Ø³ÙŠ - 6 Ø£ÙŠØ§Ù…",
                    new List<PricingConditionDto> { new("durationDays", "equals", "6") },
                    shortPackage.BasePrice,
                    "perPerson",
                    90,
                    true),
                new(
                    "rule-flex-10-default",
                    "Ø§Ù„Ø³Ø¹Ø± Ø§Ù„Ø£Ø³Ø§Ø³ÙŠ - 10 Ø£ÙŠØ§Ù… ÙØ£ÙƒØ«Ø±",
                    new List<PricingConditionDto> { new("durationDays", "equals", "10") },
                    longPackage.BasePrice,
                    "perPerson",
                    90,
                    true),
                new(
                    "rule-flex-default",
                    "Ø§Ù„Ø³Ø¹Ø± Ø§Ù„Ø§ÙØªØ±Ø§Ø¶ÙŠ",
                    Array.Empty<PricingConditionDto>(),
                    shortPackage.BasePrice,
                    "perPerson",
                    1,
                    true)
            };
        }

        private static List<TravelPackageDto> BuildDefaultPackages()
        {
            var now = DateTime.UtcNow;

            return new List<TravelPackageDto>
            {
                NormalizePackage(new TravelPackageDto(
                    "umrah-10-days",
                    "Ø¨Ø§Ù‚Ø© Ø§Ù„Ø¹Ù…Ø±Ø© 10 Ø£ÙŠØ§Ù…",
                    "Ø±Ø­Ù„Ø© Ù…ØªÙƒØ§Ù…Ù„Ø© Ù„Ø§ ØªÙ†Ø³Ù‰",
                    "Ø¨Ø§Ù‚Ø© Ø¹Ù…Ø±Ø© ØªØ´Ù…Ù„ Ø§Ù„Ø¥Ù‚Ø§Ù…Ø© ÙˆØ§Ù„ØªÙ†Ù‚Ù„Ø§Øª ÙˆØ®Ø¯Ù…Ø© Ø§Ù„Ù…ØªØ§Ø¨Ø¹Ø©.",
                    10,
                    "10 Ø£ÙŠØ§Ù…",
                    "/landingpage/avion.png",
                    95m,
                    0m,
                    "Ø¯.Ùƒ",
                    "rules",
                    PublishedStatus,
                    1,
                    new List<PackageOptionDto>
                    {
                        new("bus", "Ø¨Ø§Øµ", true, 0m, null),
                        new("private-car", "Ø³ÙŠØ§Ø±Ø© Ø®Ø§ØµØ©", true, 500m, null)
                    },
                    new List<PackageOptionDto>
                    {
                        new("double", "ØºØ±ÙØ© Ù…Ø²Ø¯ÙˆØ¬Ø©", true, 0m, null),
                        new("triple", "ØºØ±ÙØ© Ø«Ù„Ø§Ø«ÙŠØ©", true, 15m, null),
                        new("quad", "ØºØ±ÙØ© Ø±Ø¨Ø§Ø¹ÙŠØ©", true, 0m, null),
                        new("single", "ØºØ±ÙØ© ÙØ±Ø¯ÙŠØ©", true, 60m, null)
                    },
                    new List<string> { "2026-10-07", "2026-10-14", "2026-10-21" },
                    new List<PricingRuleDto>
                    {
                        new("rule-10-default", "Ø§Ù„Ø³Ø¹Ø± Ø§Ù„Ø£Ø³Ø§Ø³ÙŠ - 10 Ø£ÙŠØ§Ù…", Array.Empty<PricingConditionDto>(), 95m, "perPerson", 10, true),
                        new("rule-10-double", "ØºØ±ÙØ© Ø«Ù†Ø§Ø¦ÙŠØ© - Ø¨Ø§Øµ", new List<PricingConditionDto>
                        {
                            new("roomType", "equals", "ØºØ±ÙØ© Ù…Ø²Ø¯ÙˆØ¬Ø©"),
                            new("transport", "equals", "Ø¨Ø§Øµ")
                        }, 95m, "perPerson", 80, true)
                    },
                    null,
                    now,
                    now)),
                NormalizePackage(new TravelPackageDto(
                    "umrah-6-days",
                    "Ø¨Ø§Ù‚Ø© Ø§Ù„Ø¹Ù…Ø±Ø© 6 Ø£ÙŠØ§Ù…",
                    "Ø±Ø­Ù„Ø© Ø§Ù‚ØªØµØ§Ø¯ÙŠØ© Ù…Ø±ÙŠØ­Ø©",
                    "Ø¨Ø§Ù‚Ø© Ù‚ØµÙŠØ±Ø© Ù…Ù†Ø§Ø³Ø¨Ø© Ù„Ù„Ø­Ø¬ÙˆØ²Ø§Øª Ø§Ù„Ø³Ø±ÙŠØ¹Ø©.",
                    6,
                    "6 Ø£ÙŠØ§Ù…",
                    "/landingpage/paysage.png",
                    75m,
                    0m,
                    "Ø¯.Ùƒ",
                    "rules",
                    PublishedStatus,
                    2,
                    new List<PackageOptionDto>
                    {
                        new("bus", "Ø¨Ø§Øµ", true, 0m, null),
                        new("private-car", "Ø³ÙŠØ§Ø±Ø© Ø®Ø§ØµØ©", true, 350m, null)
                    },
                    new List<PackageOptionDto>
                    {
                        new("double", "ØºØ±ÙØ© Ù…Ø²Ø¯ÙˆØ¬Ø©", true, 0m, null),
                        new("triple", "ØºØ±ÙØ© Ø«Ù„Ø§Ø«ÙŠØ©", true, 10m, null),
                        new("quad", "ØºØ±ÙØ© Ø±Ø¨Ø§Ø¹ÙŠØ©", true, 0m, null)
                    },
                    new List<string> { "2026-10-01", "2026-10-08", "2026-10-15" },
                    new List<PricingRuleDto>
                    {
                        new("rule-6-default", "Ø§Ù„Ø³Ø¹Ø± Ø§Ù„Ø£Ø³Ø§Ø³ÙŠ - 6 Ø£ÙŠØ§Ù…", Array.Empty<PricingConditionDto>(), 75m, "perPerson", 10, true),
                        new("rule-6-double", "ØºØ±ÙØ© Ø«Ù†Ø§Ø¦ÙŠØ© - Ø¨Ø§Øµ", new List<PricingConditionDto>
                        {
                            new("roomType", "equals", "ØºØ±ÙØ© Ù…Ø²Ø¯ÙˆØ¬Ø©"),
                            new("transport", "equals", "Ø¨Ø§Øµ")
                        }, 75m, "perPerson", 80, true)
                    },
                    null,
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
        RowadPricingProfileDto? PricingProfile,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public sealed record RowadPricingProfileDto(
        bool Enabled,
        decimal VisaPrice,
        IReadOnlyDictionary<string, decimal> BusPrices,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> MakkahRoomPrices,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> MadinahRoomPrices);

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


