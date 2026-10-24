using System.Text.Json;

namespace RowadUmrahSystem.Web.Services.BookingFlow;

/// <summary>
/// Booking rules from App_Data/booking-config.json: visa fees, pack prices and nights, hotels,
/// room types and stock, thresholds. Nothing here is hard-coded in the app; the file is read on
/// each request so a change made on the server applies immediately.
/// </summary>
public sealed record BookingConfig(
    string Currency,
    string CurrencyIso,
    VisaFeesConfig VisaFees,
    int FewSeatsThreshold,
    int PaymentHoldMinutes,
    int DraftExpiryDays,
    int MinPassportValidityMonths,
    double ExtractionConfidenceThreshold,
    int RoomingFreezeDaysBeforeDeparture,
    IReadOnlyList<string> FrequentNationalities,
    IReadOnlyList<NationalityConfig> Nationalities,
    DocumentRulesConfig Documents,
    IReadOnlyList<PackBookingConfig> Packs,
    IReadOnlyList<HotelConfig> Hotels)
{
    public PackBookingConfig? Pack(string packageId) => Packs.FirstOrDefault(pack => pack.PackageId == packageId);

    public HotelConfig? Hotel(string hotelId) => Hotels.FirstOrDefault(hotel => hotel.Id == hotelId);

    public string? VisaType(string? nationality) =>
        Nationalities.FirstOrDefault(item => item.Name == nationality?.Trim())?.VisaType;
}

public sealed record VisaFeesConfig(decimal Rowad, decimal Existing);

public sealed record NationalityConfig(string Name, string VisaType);

public sealed record DocumentRulesConfig(bool CivilIdRequired, int CivilIdMinAge, bool PersonalPhotoRequired);

public sealed record PackNights(int Makkah, int Madinah);

public sealed record NationalityPrice(decimal? Adult, decimal? Child);

public sealed record PackBookingConfig(
    string PackageId,
    PackNights Nights,
    decimal? AdultPrice,
    decimal? ChildPrice,
    IReadOnlyDictionary<string, NationalityPrice>? PriceByNationality,
    decimal BusPrice,
    int SeatsPerDeparture,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Hotels)
{
    public int NightsIn(string city) => city == BookingCities.Madinah ? Nights.Madinah : Nights.Makkah;

    /// <summary>Cities of the stay in travel order: Makkah, then Madinah when the pack has nights there.</summary>
    public IReadOnlyList<string> Cities() =>
        Nights.Madinah > 0 ? new[] { BookingCities.Makkah, BookingCities.Madinah } : new[] { BookingCities.Makkah };
}

public sealed record HotelConfig(
    string Id,
    string Name,
    string City,
    int? Stars,
    string? District,
    int? WalkMinutes,
    string? Description,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<string> Photos,
    double? Latitude,
    double? Longitude,
    bool Placeholder,
    decimal SupplementPerPersonPerNight,
    IReadOnlyList<RoomTypeConfig> Rooms)
{
    public RoomTypeConfig? Room(string? type) => Rooms.FirstOrDefault(room => room.Type == type);
}

public sealed record RoomTypeConfig(
    string Type,
    int Beds,
    int RoomsPerDeparture,
    decimal FamilySupplementPerRoomPerNight,
    decimal SharedSupplementPerBedPerNight);

public static class BookingCities
{
    public const string Makkah = "makkah";
    public const string Madinah = "madinah";

    public static string Label(string city) => city == Madinah ? "المدينة المنورة" : "مكة المكرمة";

    public static string ShortLabel(string city) => city == Madinah ? "المدينة" : "مكة";
}

public sealed class BookingConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public BookingConfigStore(IWebHostEnvironment environment)
    {
        _path = Path.Combine(environment.ContentRootPath, "App_Data", "booking-config.json");
    }

    public async Task<BookingConfig> ReadAsync()
    {
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<BookingConfig>(stream, JsonOptions)
            ?? throw new InvalidOperationException("booking-config.json is empty.");
    }
}
