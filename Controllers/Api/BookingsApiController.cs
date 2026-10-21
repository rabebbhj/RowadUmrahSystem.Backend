using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    /// <summary>
    /// Bookings of the signed-in traveler account. The package, departure, transport and
    /// total are validated and computed here from App_Data/travel-packages.json.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/bookings")]
    public class BookingsApiController : ControllerBase
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        /// <summary>Visa type per nationality from Ruwad's pricing sheet; same table as the mobile app.</summary>
        public static readonly IReadOnlyDictionary<string, string> VisaTypeByNationality = new Dictionary<string, string>
        {
            ["مصري"] = "umrah", ["هندي"] = "umrah", ["بنغلاديشي"] = "umrah", ["اندونيسي"] = "umrah",
            ["تونسي"] = "umrah", ["جزائري"] = "umrah", ["فلسطيني"] = "umrah",
            ["سوري"] = "tourist", ["مالي"] = "tourist", ["سوداني"] = "tourist", ["باكستاني"] = "tourist",
            ["مغربي"] = "tourist", ["أردني"] = "tourist"
        };
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly string _packagesPath;

        public BookingsApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment environment)
        {
            _context = context;
            _userManager = userManager;
            _packagesPath = Path.Combine(environment.ContentRootPath, "App_Data", "travel-packages.json");
        }

        [HttpGet("mine")]
        public async Task<ActionResult<IReadOnlyList<BookingDto>>> GetMine()
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();

            var bookings = await QueryBookings()
                .Where(booking => booking.UserId == userId)
                .OrderByDescending(booking => booking.CreatedAt)
                .ToListAsync();

            return Ok(bookings.Select(Map).ToList());
        }

        [HttpGet("{reference}")]
        public async Task<ActionResult<BookingDto>> GetByReference(string reference)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();

            var booking = await QueryBookings().FirstOrDefaultAsync(item => item.Reference == reference && item.UserId == userId);
            return booking == null ? NotFound() : Ok(Map(booking));
        }

        [HttpPost]
        [Consumes("application/json")]
        public async Task<ActionResult<BookingDto>> Create([FromBody] CreateBookingRequest request)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();

            if (!string.IsNullOrWhiteSpace(request.ClientRequestId))
            {
                var existing = await QueryBookings().FirstOrDefaultAsync(item => item.UserId == userId && item.ClientRequestId == request.ClientRequestId);
                if (existing != null) return Ok(Map(existing));
            }

            if (!VisaTypeByNationality.TryGetValue(request.Nationality.Trim(), out var visaIssuer))
                return BadRequest(BookingError("الجنسية المختارة غير متاحة للحجز حالياً. تواصل مع رواد."));

            var package = (await ReadPublishedPackagesAsync()).FirstOrDefault(item => item.Id == request.PackageId);
            if (package == null) return BadRequest(BookingError("الباقة غير متاحة للحجز حالياً."));

            // Same list the app shows: fixed dates plus the weekly schedule, upcoming only.
            var departure = request.DepartureDate?.Trim();
            if (PackageDepartures.HasSchedule(package) &&
                (string.IsNullOrEmpty(departure) || !PackageDepartures.Upcoming(package, PackageDepartures.TodayInKuwait()).Contains(departure)))
                return BadRequest(BookingError("موعد السفر المختار غير متاح. اختر موعداً قادماً من مواعيد الباقة."));

            var transport = package.TransportOptions.FirstOrDefault(option => option.Active && option.Id == request.TransportId)
                ?? (string.IsNullOrEmpty(request.TransportId) ? package.TransportOptions.FirstOrDefault(option => option.Active) : null);
            if (transport == null) return BadRequest(BookingError("وسيلة النقل المختارة غير متاحة في هذه الباقة."));

            var travelerIds = request.Travelers.Select(traveler => traveler.TravelerId).Distinct().ToList();
            var expectedCount = request.Adults + request.Children + request.Infants;
            if (travelerIds.Count != request.Travelers.Count || travelerIds.Count != expectedCount)
                return BadRequest(BookingError("عدد المسافرين لا يطابق بيانات الحجز."));

            var travelers = await _context.Travelers.Where(traveler => travelerIds.Contains(traveler.Id) && !traveler.IsDeleted).ToListAsync();
            if (travelers.Count != travelerIds.Count) return BadRequest(BookingError("بعض المسافرين غير مسجلين. أعد إرسال بياناتهم."));
            var blocked = travelers.FirstOrDefault(traveler => traveler.IsBlocked);
            if (blocked != null) return Conflict(BookingError($"لا يمكن إتمام الحجز للمسافر {blocked.FullName}. تواصل مع رواد."));

            // Same paying-traveler count as the app: infants get a file but are not priced here.
            var payingTravelers = Math.Max(1, request.Adults + request.Children);
            var total = BookingPricing.Calculate(package, new BookingPriceContext(
                request.Nationality,
                request.RoomType,
                transport.Id == "bus" ? "باص" : transport.Label,
                payingTravelers,
                departure,
                request.HasExistingVisa,
                visaIssuer,
                package.DurationDays,
                request.Hotel));

            var now = DateTime.UtcNow;
            var booking = new Booking
            {
                Reference = await NewReferenceAsync(now),
                UserId = userId,
                ClientRequestId = string.IsNullOrWhiteSpace(request.ClientRequestId) ? null : request.ClientRequestId.Trim(),
                PackageId = package.Id,
                PackageName = package.Name,
                DurationDays = package.DurationDays,
                ImageUrl = package.ImageUrl,
                DepartureDate = string.IsNullOrEmpty(departure) ? null : departure,
                Nationality = request.Nationality.Trim(),
                Hotel = request.Hotel?.Trim(),
                RoomType = request.RoomType?.Trim(),
                Transport = transport.Label,
                HasExistingVisa = request.HasExistingVisa,
                VisaIssuer = visaIssuer,
                Adults = request.Adults,
                Children = request.Children,
                Infants = request.Infants,
                TotalAmount = total,
                Currency = package.Currency,
                CreatedAt = now,
                UpdatedAt = now,
                Travelers = request.Travelers.Select(traveler => new BookingTraveler { TravelerId = traveler.TravelerId, Kind = traveler.Kind }).ToList()
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            var created = await QueryBookings().FirstAsync(item => item.Id == booking.Id);
            return CreatedAtAction(nameof(GetByReference), new { reference = booking.Reference }, Map(created));
        }

        private IQueryable<Booking> QueryBookings() => _context.Bookings
            .AsNoTracking()
            .Include(booking => booking.Travelers)
            .ThenInclude(link => link.Traveler);

        private async Task<string> NewReferenceAsync(DateTime now)
        {
            while (true)
            {
                var reference = $"RW-{now:yyMMdd}-{RandomNumberGenerator.GetInt32(1000, 10000)}";
                if (!await _context.Bookings.AnyAsync(booking => booking.Reference == reference)) return reference;
            }
        }

        private async Task<IReadOnlyList<TravelPackageDto>> ReadPublishedPackagesAsync()
        {
            if (!System.IO.File.Exists(_packagesPath)) return Array.Empty<TravelPackageDto>();
            await using var stream = System.IO.File.OpenRead(_packagesPath);
            var packages = await JsonSerializer.DeserializeAsync<List<TravelPackageDto>>(stream, JsonOptions);
            return (packages ?? new List<TravelPackageDto>()).Where(package => package.Status == "published").ToList();
        }

        private static ProblemDetails BookingError(string message) => new() { Title = message, Detail = message };

        private static BookingDto Map(Booking booking) => new(
            booking.Reference,
            booking.PackageId,
            booking.PackageName,
            booking.DurationDays,
            booking.ImageUrl,
            booking.DepartureDate,
            booking.Nationality,
            booking.Hotel,
            booking.RoomType,
            booking.Transport,
            booking.HasExistingVisa,
            booking.Adults,
            booking.Children,
            booking.Infants,
            booking.TotalAmount,
            booking.Currency,
            booking.Status,
            booking.PaymentStatus,
            booking.CreatedAt,
            booking.Travelers.Select(link => new BookingTravelerDto(link.TravelerId, link.Traveler.FullName, link.Kind)).ToList());
    }

    public sealed class CreateBookingRequest
    {
        [StringLength(64)] public string? ClientRequestId { get; set; }
        [Required, StringLength(64)] public string PackageId { get; set; } = "";
        [StringLength(20)] public string? DepartureDate { get; set; }
        [Required, StringLength(60)] public string Nationality { get; set; } = "";
        [StringLength(120)] public string? Hotel { get; set; }
        [StringLength(200)] public string? RoomType { get; set; }
        [StringLength(64)] public string? TransportId { get; set; }
        public bool HasExistingVisa { get; set; }
        [StringLength(40)] public string? VisaIssuer { get; set; }
        [Range(1, 20)] public int Adults { get; set; }
        [Range(0, 20)] public int Children { get; set; }
        [Range(0, 20)] public int Infants { get; set; }
        [Required, MinLength(1), MaxLength(60)] public List<CreateBookingTraveler> Travelers { get; set; } = new();
    }

    public sealed class CreateBookingTraveler
    {
        [Range(1, int.MaxValue)] public int TravelerId { get; set; }
        [Required, RegularExpression("^(adult|child|infant)$")] public string Kind { get; set; } = "adult";
    }

    public sealed record BookingTravelerDto(int TravelerId, string FullName, string Kind);

    public sealed record BookingDto(
        string Reference,
        string PackageId,
        string PackageName,
        int DurationDays,
        string? ImageUrl,
        string? DepartureDate,
        string Nationality,
        string? Hotel,
        string? RoomType,
        string? Transport,
        bool HasExistingVisa,
        int Adults,
        int Children,
        int Infants,
        decimal TotalAmount,
        string Currency,
        string Status,
        string PaymentStatus,
        DateTime CreatedAt,
        IReadOnlyList<BookingTravelerDto> Travelers);
}
