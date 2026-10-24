using System.ComponentModel.DataAnnotations;
using System.Globalization;
using RowadUmrahSystem.Web.Controllers.Api;

namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>One traveler of the draft. Kind: man, woman, child, infant. Visa: rowad or existing.</summary>
public sealed class DraftTraveler
{
    [Range(0, int.MaxValue)] public int? TravelerId { get; set; }
    [Required, RegularExpression("^(man|woman|child|infant)$")] public string Kind { get; set; } = "man";
    /// <summary>male or female; read from the passport at step 4 (needed for children in shared rooms).</summary>
    [RegularExpression("^(male|female)?$")] public string? Gender { get; set; }
    [StringLength(60)] public string? Nationality { get; set; }
    [Required, RegularExpression("^(rowad|existing)$")] public string VisaOption { get; set; } = "rowad";
    [StringLength(10)] public string? BirthDate { get; set; }
    [StringLength(10)] public string? PassportExpiry { get; set; }
    [StringLength(30)] public string? PassportNumber { get; set; }
}

public sealed class DraftRoomQuantity
{
    [Required, StringLength(20)] public string Type { get; set; } = "";
    [Range(0, 30)] public int Quantity { get; set; }
}

public sealed class DraftSharedBeds
{
    [StringLength(20)] public string? Men { get; set; }
    [StringLength(20)] public string? Women { get; set; }
}

/// <summary>Hotel and rooms in one city. Family: private rooms by quantity; otherwise one shared-bed type per sex.</summary>
public sealed class DraftStay
{
    [Required, RegularExpression("^(makkah|madinah)$")] public string City { get; set; } = BookingCities.Makkah;
    [Required, StringLength(60)] public string HotelId { get; set; } = "";
    [MaxLength(10)] public List<DraftRoomQuantity> Rooms { get; set; } = new();
    public DraftSharedBeds? SharedBeds { get; set; }
}

public class BookingDraftRequest
{
    [Required, StringLength(64)] public string PackageId { get; set; } = "";
    [StringLength(20)] public string? DepartureDate { get; set; }
    [Required, StringLength(60)] public string Nationality { get; set; } = "";
    public bool FamilyMode { get; set; }
    [Required, MinLength(1), MaxLength(60)] public List<DraftTraveler> Travelers { get; set; } = new();
    [MaxLength(2)] public List<DraftStay> Stays { get; set; } = new();
}

public sealed record QuoteLine(string Id, string Label, string Detail, decimal Amount);

/// <summary>Severity "error" blocks the booking; "notice" is shown before paying (e.g. a corrected type).</summary>
public sealed record QuoteIssue(string Code, string Severity, string Message, int? TravelerIndex = null);

/// <summary>Traveler as the server prices it: kind and gender may be corrected from the passport data.</summary>
public sealed record QuotedTraveler(int Index, string Kind, string? Gender, string Nationality, string VisaType, string VisaOption, decimal VisaFee);

public sealed record BookingQuoteResult(
    bool Valid,
    decimal Total,
    string Currency,
    string? DepartureDate,
    string? ReturnDate,
    int Men,
    int Women,
    int Children,
    int Infants,
    IReadOnlyList<QuoteLine> Lines,
    IReadOnlyList<QuoteIssue> Issues,
    IReadOnlyList<QuotedTraveler> Travelers);

/// <summary>
/// Server-side total of a booking (spec § 4):
/// Total = A·P_adult + E·P_child + (A+E)·Bus + Σ Visa_v + hotel supplement + room supplements.
/// Infants pay only their visa (no pack price, no bus, no bed). Pure function: stock and seats,
/// which need the database, are checked by the controller.
/// </summary>
public static class BookingQuote
{
    public const string RowadVisa = "rowad";
    public const string ExistingVisa = "existing";

    public static bool IsAdult(string kind) => kind is "man" or "woman";

    public static bool IsPaying(string kind) => kind != "infant";

    public static BookingQuoteResult Calculate(BookingConfig config, TravelPackageDto package, BookingDraftRequest draft, DateOnly today, bool requireStays)
    {
        var issues = new List<QuoteIssue>();
        var pack = config.Pack(package.Id);
        if (pack == null)
        {
            issues.Add(Error("pack", "هذه الباقة غير متاحة للحجز عبر التطبيق حالياً."));
            return Empty(config, draft, issues);
        }

        var departure = draft.DepartureDate?.Trim();
        DateOnly? departureDate = DateOnly.TryParseExact(departure, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
        if (PackageDepartures.HasSchedule(package) && (departure == null || !PackageDepartures.Upcoming(package, today).Contains(departure)))
            issues.Add(Error("departure", "موعد السفر المختار غير متاح. اختر موعداً قادماً."));
        var returnDate = departureDate?.AddDays(package.DurationDays);

        if (config.VisaType(draft.Nationality) == null)
            issues.Add(Error("nationality", "الجنسية المختارة غير متاحة للحجز حالياً. تواصل مع روّاد."));

        // Travelers: kind, gender and nationality from the passport when known.
        var travelers = new List<QuotedTraveler>();
        for (var index = 0; index < draft.Travelers.Count; index++)
        {
            var traveler = draft.Travelers[index];
            var kind = traveler.Kind;
            var gender = traveler.Gender is "male" or "female" ? traveler.Gender : kind switch { "man" => "male", "woman" => "female", _ => null };
            if (DateOnly.TryParseExact(traveler.BirthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth) && departureDate.HasValue)
            {
                var age = AgeAt(birth, departureDate.Value);
                var byAge = age < 2 ? "infant" : age < 12 ? "child" : gender == "female" ? "woman" : gender == "male" ? "man" : IsAdult(kind) ? kind : "man";
                if (byAge != kind)
                {
                    issues.Add(Notice("kind-corrected", $"المسافر {index + 1}: عمره عند السفر {age} سنة، فاحتسبناه «{KindLabel(byAge)}» بدل «{KindLabel(kind)}».", index));
                    kind = byAge;
                }
            }
            else if (IsAdult(kind) && gender != null && gender != (kind == "man" ? "male" : "female"))
            {
                var corrected = gender == "male" ? "man" : "woman";
                issues.Add(Notice("gender-corrected", $"المسافر {index + 1}: الجنس في الجواز «{KindLabel(corrected)}»، صحّحناه.", index));
                kind = corrected;
            }

            var nationality = string.IsNullOrWhiteSpace(traveler.Nationality) ? draft.Nationality.Trim() : traveler.Nationality.Trim();
            var visaType = config.VisaType(nationality);
            if (visaType == null)
            {
                issues.Add(Error("traveler-nationality", $"المسافر {index + 1}: جنسية «{nationality}» غير متاحة للحجز حالياً. تواصل مع روّاد.", index));
                visaType = "";
            }

            var fee = traveler.VisaOption == ExistingVisa ? config.VisaFees.Existing : config.VisaFees.Rowad;
            travelers.Add(new QuotedTraveler(index, kind, gender, nationality, visaType, traveler.VisaOption, fee));

            if (returnDate.HasValue && DateOnly.TryParseExact(traveler.PassportExpiry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)
                && expiry < returnDate.Value.AddMonths(config.MinPassportValidityMonths))
                issues.Add(Error("passport-validity", $"المسافر {index + 1}: يجب أن يبقى الجواز صالحاً {config.MinPassportValidityMonths} أشهر على الأقل بعد العودة.", index));
        }

        var passports = draft.Travelers.Select(item => item.PassportNumber?.Trim().ToUpperInvariant()).Where(value => !string.IsNullOrEmpty(value)).ToList();
        if (passports.Count != passports.Distinct().Count())
            issues.Add(Error("duplicate-passport", "رقم الجواز نفسه مكرّر لمسافرَين في هذا الحجز."));

        var men = travelers.Count(item => item.Kind == "man");
        var women = travelers.Count(item => item.Kind == "woman");
        var children = travelers.Count(item => item.Kind == "child");
        var infants = travelers.Count(item => item.Kind == "infant");
        var adults = men + women;
        var paying = adults + children;
        if (adults < 1) issues.Add(Error("adult", "يجب أن يضم الحجز مسافراً بالغاً واحداً على الأقل."));
        if (infants > adults) issues.Add(Error("infants", "عدد الرضّع لا يتجاوز عدد البالغين."));

        // Pack price per traveler, by the nationality read from the passport when configured.
        var packParts = new List<(decimal Unit, string Kind)>();
        foreach (var traveler in travelers.Where(item => IsPaying(item.Kind)))
        {
            var byNationality = pack.PriceByNationality != null && pack.PriceByNationality.TryGetValue(traveler.Nationality, out var price) ? price : null;
            var adult = byNationality?.Adult ?? pack.AdultPrice ?? package.BasePrice;
            var unit = traveler.Kind == "child" ? byNationality?.Child ?? pack.ChildPrice ?? adult : adult;
            packParts.Add((unit, traveler.Kind));
        }

        var lines = new List<QuoteLine>
        {
            new("pack", $"الباقة · {CountLabel(paying, "مسافر", "مسافرين")}", Breakdown(packParts.Select(part => part.Unit)), packParts.Sum(part => part.Unit)),
            new("bus", "الباص (إجباري)", $"{paying} × {Amount(pack.BusPrice)}", paying * pack.BusPrice),
            new("visa", $"رسوم التأشيرة · {travelers.Count}", Breakdown(travelers.Select(item => item.VisaFee)), travelers.Sum(item => item.VisaFee))
        };

        // Hotels and rooms, city by city.
        decimal supplements = 0;
        var supplementDetails = new List<string>();
        var stays = draft.Stays ?? new List<DraftStay>();
        foreach (var city in pack.Cities())
        {
            var stay = stays.FirstOrDefault(item => item.City == city);
            if (stay == null)
            {
                if (requireStays) issues.Add(Error("stay", $"اختر الفندق والغرف في {BookingCities.Label(city)}."));
                continue;
            }

            var allowed = pack.Hotels.TryGetValue(city, out var ids) ? ids : Array.Empty<string>();
            var hotel = allowed.Contains(stay.HotelId) ? config.Hotel(stay.HotelId) : null;
            if (hotel == null || hotel.City != city)
            {
                issues.Add(Error("hotel", $"الفندق المختار غير متاح في {BookingCities.Label(city)}."));
                continue;
            }

            var nights = pack.NightsIn(city);
            var stayAmount = hotel.SupplementPerPersonPerNight * nights * paying;
            if (draft.FamilyMode)
            {
                var beds = 0;
                foreach (var line in stay.Rooms.Where(item => item.Quantity > 0))
                {
                    var room = hotel.Room(line.Type);
                    if (room == null)
                    {
                        issues.Add(Error("room-type", $"نوع الغرفة غير متاح في {hotel.Name}."));
                        continue;
                    }
                    beds += room.Beds * line.Quantity;
                    stayAmount += room.FamilySupplementPerRoomPerNight * nights * line.Quantity;
                }
                if (beds < paying) issues.Add(Error("beds-missing", $"{BookingCities.ShortLabel(city)}: ينقص {paying - beds} {(paying - beds == 1 ? "سرير" : "أسرّة")}."));
                else if (beds - paying > 1) issues.Add(Error("beds-extra", $"{BookingCities.ShortLabel(city)}: أسرّة زائدة عن العدد."));
            }
            else
            {
                // Children sleep in the room of an adult of the same sex from the booking.
                var boys = travelers.Count(item => item.Kind == "child" && item.Gender == "male");
                var girls = travelers.Count(item => item.Kind == "child" && item.Gender == "female");
                if (travelers.Any(item => item.Kind == "child" && item.Gender == null))
                    issues.Add(Error("child-gender", "أكمل بيانات جوازات الأطفال لنوزّع الغرف."));
                if ((boys > 0 && men == 0) || (girls > 0 && women == 0))
                    issues.Add(Error("child-needs-family", "الطفل يقيم مع بالغ من جنسه في الحجز؛ اختر «نحن عائلة»."));

                foreach (var (count, type, label) in new[] { (men + boys, stay.SharedBeds?.Men, "الرجال"), (women + girls, stay.SharedBeds?.Women, "النساء") })
                {
                    if (count == 0) continue;
                    var room = hotel.Room(type);
                    if (room == null)
                    {
                        issues.Add(Error("shared-bed", $"{BookingCities.ShortLabel(city)}: اختر نوع الغرفة المشتركة لـ{label}."));
                        continue;
                    }
                    stayAmount += room.SharedSupplementPerBedPerNight * nights * count;
                }
            }

            supplements += stayAmount;
            if (stayAmount > 0) supplementDetails.Add($"{hotel.Name} {Amount(stayAmount)}");
        }

        if (requireStays || stays.Count > 0)
            lines.Add(new QuoteLine("supplements", "فرق الفندق والغرف", supplementDetails.Count == 0 ? "متضمَّن في الباقة" : string.Join(" + ", supplementDetails), supplements));

        var total = lines.Sum(line => line.Amount);
        return new BookingQuoteResult(
            issues.All(issue => issue.Severity != "error"),
            total,
            config.Currency,
            departure,
            returnDate?.ToString("yyyy-MM-dd"),
            men, women, children, infants,
            lines,
            issues,
            travelers);
    }

    public static int AgeAt(DateOnly birth, DateOnly on)
    {
        var age = on.Year - birth.Year;
        if (on < birth.AddYears(age)) age--;
        return age;
    }

    public static string KindLabel(string kind) => kind switch
    {
        "man" => "رجل",
        "woman" => "امرأة",
        "child" => "طفل",
        _ => "رضيع"
    };

    public static string Amount(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string CountLabel(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    /// <summary>"2 × 95 + 1 × 75": unit amounts grouped, largest first.</summary>
    private static string Breakdown(IEnumerable<decimal> units) => string.Join(" + ", units
        .GroupBy(unit => unit)
        .OrderByDescending(group => group.Key)
        .Select(group => $"{group.Count()} × {Amount(group.Key)}"));

    private static QuoteIssue Error(string code, string message, int? index = null) => new(code, "error", message, index);

    private static QuoteIssue Notice(string code, string message, int? index = null) => new(code, "notice", message, index);

    private static BookingQuoteResult Empty(BookingConfig config, BookingDraftRequest draft, List<QuoteIssue> issues) =>
        new(false, 0, config.Currency, draft.DepartureDate, null, 0, 0, 0, 0, Array.Empty<QuoteLine>(), issues, Array.Empty<QuotedTraveler>());
}
