using System.Globalization;
using RowadUmrahSystem.Web.Controllers.Api;

namespace RowadUmrahSystem.Web.Services;

public sealed record BookingPriceContext(
    string? Nationality,
    string? RoomType,
    string? Transport,
    int Travelers,
    string? DepartureDate,
    bool HasExistingVisa,
    string? VisaIssuer,
    int DurationDays,
    string? Hotel);

/// <summary>
/// Server-side source of truth for a booking total. Mirrors calculatePackagePrice in the
/// mobile app (src/api/packages.ts) so the estimate shown to the traveler matches what is stored.
/// </summary>
public static class BookingPricing
{
    private const decimal DefaultVisaSupplement = 45m;
    private const decimal VisaFeeWhenAlreadyHasVisa = 5m;

    public static decimal Calculate(TravelPackageDto package, BookingPriceContext context)
    {
        var profilePrice = CalculateProfilePrice(package, context);
        if (profilePrice.HasValue) return Math.Round(profilePrice.Value, MidpointRounding.AwayFromZero);

        var travelers = Math.Max(1, context.Travelers);
        var visaSupplement = context.HasExistingVisa
            ? VisaFeeWhenAlreadyHasVisa
            : package.VisaSupplement > 0 ? package.VisaSupplement : DefaultVisaSupplement;
        var optionSupplement = OptionSupplement(package.RoomTypes, context.RoomType)
            + OptionSupplement(package.TransportOptions, context.Transport)
            + visaSupplement;

        if (package.PriceMode != "rules") return package.BasePrice + optionSupplement;

        var matching = package.PricingRules
            .Where(rule => rule.Active && rule.Conditions.All(condition => ConditionMatches(condition, context)))
            .OrderByDescending(rule => rule.Priority)
            .ThenByDescending(rule => rule.Conditions.Count)
            .ToList();
        var conditional = matching.Where(rule => rule.Conditions.Count > 0).ToList();
        if (conditional.Count > 0) return conditional.Sum(rule => RuleTotal(rule, travelers));
        return matching.Count > 0 ? RuleTotal(matching[0], travelers) : package.BasePrice;
    }

    private static decimal? CalculateProfilePrice(TravelPackageDto package, BookingPriceContext context)
    {
        var profile = package.PricingProfile;
        if (profile is not { Enabled: true }) return null;

        var durationDays = context.DurationDays > 0 ? context.DurationDays : package.DurationDays;
        var durationKey = durationDays.ToString(CultureInfo.InvariantCulture);
        var travelers = Math.Max(1, context.Travelers);
        var roomParts = (context.RoomType ?? "").Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (roomParts.Count == 0)
        {
            var fallback = package.RoomTypes.FirstOrDefault(room => room.Active)?.Label;
            if (!string.IsNullOrEmpty(fallback)) roomParts.Add(fallback);
        }

        var assigned = 0;
        decimal roomTotal = 0;
        for (var index = 0; index < roomParts.Count; index++)
        {
            var label = roomParts[index];
            var room = package.RoomTypes.FirstOrDefault(item => item.Label == label);
            var capacity = Math.Max(1, RoomCapacity(room?.Id, label));
            var remaining = Math.Max(0, travelers - assigned);
            var roomTravelers = index == roomParts.Count - 1
                ? (remaining > 0 ? remaining : Math.Min(capacity, travelers))
                : Math.Min(capacity, remaining > 0 ? remaining : capacity);
            var makkah = ProfileRoomPrice(profile.MakkahRoomPrices, durationKey, label) is var m and > 0 ? m : DefaultRoomPrice(durationDays, makkah: true, capacity);
            var madinah = ProfileRoomPrice(profile.MadinahRoomPrices, durationKey, label) is var d and > 0 ? d : DefaultRoomPrice(durationDays, makkah: false, capacity);
            assigned += roomTravelers;
            roomTotal += (makkah + madinah) * roomTravelers;
        }

        var roomAmount = roomTotal > 0 ? roomTotal : package.BasePrice;
        var transport = context.Transport == "باص" && profile.BusPrices.TryGetValue(durationKey, out var bus) ? bus : 0m;
        var visa = context.HasExistingVisa ? VisaFeeWhenAlreadyHasVisa : profile.VisaPrice;
        return roomAmount + (transport + visa) * travelers;
    }

    private static decimal ProfileRoomPrice(IReadOnlyDictionary<string, IReadOnlyDictionary<string, decimal>> prices, string durationKey, string label) =>
        prices.TryGetValue(durationKey, out var byRoom) && byRoom.TryGetValue(label, out var price) ? price : 0m;

    private static int RoomCapacity(string? id, string label)
    {
        id = id?.ToLowerInvariant() ?? "";
        if (id.Contains("quad") || label.Contains("رباع")) return 4;
        if (id.Contains("triple") || label.Contains("ثلاث")) return 3;
        if (id.Contains("double") || label.Contains("ثنائ") || label.Contains("مزدوج")) return 2;
        return 1;
    }

    private static decimal DefaultRoomPrice(int durationDays, bool makkah, int capacity)
    {
        var index = capacity switch { 4 => 0, 3 => 1, 2 => 2, _ => 3 };
        decimal[] prices = (makkah, durationDays == 6) switch
        {
            (true, true) => [25, 30, 35, 40],
            (true, false) => [35, 40, 45, 50],
            (false, true) => [0, 0, 0, 0],
            (false, false) => [55, 60, 65, 70]
        };
        return prices[index];
    }

    private static decimal OptionSupplement(IReadOnlyList<PackageOptionDto> options, string? label) =>
        string.IsNullOrEmpty(label) ? 0m : options.FirstOrDefault(option => option.Active && option.Label == label)?.Supplement ?? 0m;

    private static decimal RuleTotal(PricingRuleDto rule, int travelers)
    {
        var priceType = rule.PriceType.Trim().ToLowerInvariant();
        var perPerson = priceType == "perperson" || priceType.Contains("person") || priceType.Contains("شخص");
        return rule.Price * (perPerson ? travelers : 1);
    }

    private static bool ConditionMatches(PricingConditionDto condition, BookingPriceContext context)
    {
        var current = condition.Field switch
        {
            "nationality" => context.Nationality,
            "roomType" => context.RoomType,
            "transport" => context.Transport,
            "travelers" => context.Travelers.ToString(CultureInfo.InvariantCulture),
            "departureDate" => context.DepartureDate,
            "previousVisa" => context.HasExistingVisa ? "yes" : "no",
            "visaIssuer" => context.VisaIssuer,
            "durationDays" => context.DurationDays.ToString(CultureInfo.InvariantCulture),
            "hotel" => context.Hotel,
            _ => null
        };
        var expected = condition.Value;
        if (string.IsNullOrEmpty(expected)) return true;
        if (string.IsNullOrEmpty(current)) return false;

        // Like JavaScript Number(): a non-numeric value makes every comparison false (lifted operators on null).
        static decimal? Number(string value) => decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
        var list = expected.Split(',').Select(item => item.Trim());
        switch (condition.Operator)
        {
            case "notEquals": return current != expected;
            case "in": return list.Contains(current);
            case "notIn": return !list.Contains(current);
            case "gt": return Number(current) > Number(expected);
            case "lt": return Number(current) < Number(expected);
            case "range":
                var bounds = expected.Split(',');
                return bounds.Length == 2 && Number(current) >= Number(bounds[0]) && Number(current) <= Number(bounds[1]);
            default: return current == expected;
        }
    }
}
