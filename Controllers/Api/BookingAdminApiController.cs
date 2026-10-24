using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services.BookingFlow;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    /// <summary>
    /// Back-office of the booking flow (spec § 5.5 and § 8.3): booking settings (prices, visa
    /// fees, hotels, photos, room stock, thresholds), rooming list per departure, locks, manual
    /// moves, rebalancing, Excel export and cancellation.
    /// </summary>
    [ApiController]
    [Authorize(Roles = "Admin")]
    [Route("api/admin/booking")]
    public class BookingAdminApiController : ControllerBase
    {
        private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
        private readonly ApplicationDbContext _context;
        private readonly BookingConfigStore _configStore;
        private readonly BookingFlowService _flow;
        private readonly IWebHostEnvironment _environment;

        public BookingAdminApiController(ApplicationDbContext context, BookingConfigStore configStore, BookingFlowService flow, IWebHostEnvironment environment)
        {
            _context = context;
            _configStore = configStore;
            _flow = flow;
            _environment = environment;
        }

        [HttpGet("config")]
        public async Task<ActionResult<BookingConfig>> GetConfig() => Ok(await _configStore.ReadAsync());

        [HttpPut("config")]
        public async Task<ActionResult<BookingConfig>> SaveConfig([FromBody] BookingConfig config)
        {
            var problem = Validate(config);
            if (problem != null) return BadRequest(Message(problem));
            await _configStore.WriteAsync(config);
            return Ok(await _configStore.ReadAsync());
        }

        /// <summary>Hotel photo (JPG, PNG or WebP, up to 5 MB): saved under /uploads/hotels, URL returned.</summary>
        [HttpPost("hotel-photos")]
        [RequestSizeLimit(6 * 1024 * 1024)]
        public async Task<ActionResult<object>> UploadHotelPhoto([FromForm] IFormFile? photo)
        {
            if (photo == null || photo.Length == 0) return BadRequest(Message("اختر صورة."));
            var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
            if (!PhotoExtensions.Contains(extension) || photo.Length > 5 * 1024 * 1024) return BadRequest(Message("الصورة بصيغة JPG أو PNG أو WebP وبحجم 5 ميغابايت على الأكثر."));
            var folder = Path.Combine(_environment.WebRootPath, "uploads", "hotels");
            Directory.CreateDirectory(folder);
            var name = $"{Guid.NewGuid():N}{extension}";
            await using (var stream = System.IO.File.Create(Path.Combine(folder, name))) await photo.CopyToAsync(stream);
            return Ok(new { url = $"/uploads/hotels/{name}" });
        }

        /// <summary>Departures that have bookings, newest first, with counts.</summary>
        [HttpGet("departures")]
        public async Task<ActionResult<IReadOnlyList<object>>> Departures()
        {
            var rows = await _context.Bookings.AsNoTracking()
                .Where(booking => booking.DepartureDate != null && booking.Status != BookingStatuses.Expired)
                .GroupBy(booking => new { booking.PackageId, booking.PackageName, booking.DepartureDate })
                .Select(group => new
                {
                    group.Key.PackageId,
                    group.Key.PackageName,
                    DepartureDate = group.Key.DepartureDate!,
                    Confirmed = group.Count(item => item.Status == BookingStatuses.Confirmed),
                    Pending = group.Count(item => item.Status == BookingStatuses.PendingPayment),
                    Seats = group.Where(item => item.Status == BookingStatuses.Confirmed).Sum(item => item.Adults + item.Children)
                })
                .ToListAsync();
            return Ok(rows.OrderByDescending(row => row.DepartureDate).ToList());
        }

        /// <summary>Rooming list of a departure: rooms by hotel and type, occupants, free beds, mixed rooms.</summary>
        [HttpGet("rooming")]
        public async Task<ActionResult<object>> Rooming([FromQuery, Required] string packageId, [FromQuery, Required] string departureDate)
        {
            var config = await _configStore.ReadAsync();
            await _flow.ReleaseExpiredHoldsAsync();
            var bookings = await _context.Bookings.AsNoTracking()
                .Include(booking => booking.Travelers).ThenInclude(link => link.Traveler)
                .Where(booking => booking.PackageId == packageId && booking.DepartureDate == departureDate &&
                    (booking.Status == BookingStatuses.Confirmed || booking.Status == BookingStatuses.PendingPayment))
                .ToListAsync();
            var ids = bookings.Select(booking => booking.Id).ToList();
            var assignments = await _context.RoomAssignments.AsNoTracking().Where(item => ids.Contains(item.BookingId)).ToListAsync();
            var names = bookings.SelectMany(booking => booking.Travelers).GroupBy(link => link.TravelerId).ToDictionary(group => group.Key, group => group.First().Traveler.FullName);
            var references = bookings.ToDictionary(booking => booking.Id, booking => booking.Reference);
            var pending = bookings.Where(booking => booking.Status != BookingStatuses.Confirmed).Select(booking => booking.Id).ToHashSet();

            var pools = assignments
                .GroupBy(item => new { item.City, item.HotelId, item.RoomType })
                .OrderBy(group => group.Key.City).ThenBy(group => group.Key.HotelId).ThenBy(group => group.Key.RoomType)
                .Select(group =>
                {
                    var hotel = config.Hotel(group.Key.HotelId);
                    var room = hotel?.Room(group.Key.RoomType);
                    var beds = room?.Beds ?? 0;
                    return new
                    {
                        group.Key.City,
                        group.Key.HotelId,
                        HotelName = hotel?.Name ?? group.Key.HotelId,
                        group.Key.RoomType,
                        Label = RoomLabels.Of(group.Key.RoomType),
                        Beds = beds,
                        Capacity = room?.RoomsPerDeparture ?? 0,
                        Rooms = group.GroupBy(item => item.RoomNumber).OrderBy(byRoom => byRoom.Key).Select(byRoom =>
                        {
                            var family = byRoom.Any(item => item.IsFamily);
                            var nationalities = byRoom.Select(item => item.Nationality).Distinct().ToList();
                            return new
                            {
                                Number = byRoom.Key,
                                Family = family,
                                Gender = family ? null : byRoom.First().Gender,
                                Locked = byRoom.All(item => item.IsLocked),
                                Free = family ? 0 : beds - byRoom.Count(),
                                Mixed = !family && nationalities.Count > 1,
                                Nationalities = nationalities,
                                Occupants = byRoom.Select(item => new
                                {
                                    AssignmentId = item.Id,
                                    item.BookingId,
                                    Reference = references.GetValueOrDefault(item.BookingId),
                                    item.TravelerId,
                                    Name = names.GetValueOrDefault(item.TravelerId) ?? $"#{item.TravelerId}",
                                    item.Nationality,
                                    item.Gender,
                                    Locked = item.IsLocked,
                                    Pending = pending.Contains(item.BookingId)
                                }).ToList()
                            };
                        }).ToList()
                    };
                }).ToList();

            var placed = assignments.Select(item => item.BookingId).ToHashSet();
            var moves = await _context.RoomMoveLogs.AsNoTracking()
                .Where(move => move.PackageId == packageId && move.DepartureDate == departureDate)
                .OrderByDescending(move => move.MovedAt).Take(60).ToListAsync();
            return Ok(new
            {
                Frozen = BookingFlowService.IsFrozen(departureDate, config),
                FreezeDate = DateOnly.TryParseExact(departureDate, "yyyy-MM-dd", out var date) ? date.AddDays(-config.RoomingFreezeDaysBeforeDeparture).ToString("yyyy-MM-dd") : null,
                Pools = pools,
                Bookings = bookings.OrderBy(booking => booking.CreatedAt).Select(booking => new
                {
                    booking.Reference,
                    booking.Status,
                    booking.PaymentStatus,
                    booking.FamilyMode,
                    booking.Men,
                    booking.Women,
                    booking.Children,
                    booking.Infants,
                    booking.TotalAmount,
                    booking.Currency,
                    booking.NeedsRoomReview,
                    Placed = placed.Contains(booking.Id),
                    Travelers = booking.Travelers.Select(link => new { link.TravelerId, link.Traveler.FullName, link.Kind, link.Gender, link.Nationality, link.VisaOption }).ToList()
                }).ToList(),
                Moves = moves.Select(move => new { move.TravelerId, Name = names.GetValueOrDefault(move.TravelerId), move.City, move.HotelId, move.RoomType, move.FromRoom, move.ToRoom, move.Reason, move.MovedBy, move.MovedAt }).ToList()
            });
        }

        /// <summary>Locks or unlocks one traveler: a locked traveler is never moved automatically.</summary>
        [HttpPost("assignments/{id:int}/lock")]
        public async Task<IActionResult> LockTraveler(int id, [FromBody] LockRequest request)
        {
            var assignment = await _context.RoomAssignments.FindAsync(id);
            if (assignment == null) return NotFound();
            assignment.IsLocked = request.Locked;
            assignment.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>Locks or unlocks a whole room.</summary>
        [HttpPost("rooms/lock")]
        public async Task<IActionResult> LockRoom([FromBody] RoomLockRequest request)
        {
            var assignments = await RoomQuery(request.PackageId, request.DepartureDate, request.City, request.HotelId, request.RoomType, request.RoomNumber).ToListAsync();
            if (assignments.Count == 0) return NotFound();
            foreach (var assignment in assignments)
            {
                assignment.IsLocked = request.Locked;
                assignment.UpdatedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            return NoContent();
        }

        /// <summary>
        /// Moves a traveler by hand to another room of the same hotel and type: same sex, a free bed,
        /// never into a family room of another booking. Written to the move log.
        /// </summary>
        [HttpPost("assignments/{id:int}/move")]
        public async Task<IActionResult> Move(int id, [FromBody] MoveRequest request)
        {
            var config = await _configStore.ReadAsync();
            var error = await _flow.WithBedsLockAsync(async () =>
            {
                var assignment = await _context.RoomAssignments.FindAsync(id);
                if (assignment == null) return "السرير غير موجود.";
                if (assignment.IsFamily) return "لا يُنقل مسافر من غرفة عائلية؛ عدّل حجز العائلة بدلاً من ذلك.";
                var room = config.Hotel(assignment.HotelId)?.Room(assignment.RoomType);
                if (room == null) return "نوع الغرفة لم يعد في الإعدادات.";
                if (request.RoomNumber < 1 || request.RoomNumber > room.RoomsPerDeparture) return $"رقم الغرفة بين 1 و{room.RoomsPerDeparture}.";
                if (request.RoomNumber == assignment.RoomNumber) return null;

                var now = DateTime.UtcNow;
                var holding = BookingFlowService.Holding(_context.Bookings, now).Select(booking => booking.Id);
                var occupants = await RoomQuery(assignment.PackageId, assignment.DepartureDate, assignment.City, assignment.HotelId, assignment.RoomType, request.RoomNumber)
                    .Where(item => holding.Contains(item.BookingId)).ToListAsync();
                if (occupants.Any(item => item.IsFamily)) return "هذه غرفة عائلية خاصة.";
                if (occupants.Any(item => item.Gender != assignment.Gender)) return "الغرفة لجنس آخر.";
                if (occupants.Count >= room.Beds) return "لا يوجد سرير فارغ في هذه الغرفة.";

                _context.RoomMoveLogs.Add(new RoomMoveLog
                {
                    BookingId = assignment.BookingId,
                    TravelerId = assignment.TravelerId,
                    PackageId = assignment.PackageId,
                    DepartureDate = assignment.DepartureDate,
                    City = assignment.City,
                    HotelId = assignment.HotelId,
                    RoomType = assignment.RoomType,
                    FromRoom = assignment.RoomNumber,
                    ToRoom = request.RoomNumber,
                    Reason = string.IsNullOrWhiteSpace(request.Reason) ? "manual" : request.Reason.Trim(),
                    MovedBy = User.Identity?.Name ?? "admin",
                    MovedAt = now
                });
                assignment.RoomNumber = request.RoomNumber;
                assignment.IsLocked = request.Lock ?? assignment.IsLocked;
                assignment.UpdatedAt = now;
                await _context.SaveChangesAsync();
                return null;
            });
            return error == null ? NoContent() : BadRequest(Message(error));
        }

        /// <summary>Runs the nationality rebalancing of a departure again (not after the freeze).</summary>
        [HttpPost("rebalance")]
        public async Task<IActionResult> Rebalance([FromBody] DepartureRequest request)
        {
            var config = await _configStore.ReadAsync();
            if (BookingFlowService.IsFrozen(request.DepartureDate, config)) return BadRequest(Message("قائمة الغرف مجمّدة لهذا الموعد؛ لا إعادة توزيع تلقائية بعد التجميد."));
            await _flow.RebalanceAsync(request.PackageId, request.DepartureDate, config);
            return NoContent();
        }

        /// <summary>Places again a paid booking that could not get its beds (NeedsRoomReview).</summary>
        [HttpPost("bookings/{reference}/place")]
        public async Task<IActionResult> Place(string reference)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(item => item.Reference == reference);
            if (booking == null) return NotFound();
            var config = await _configStore.ReadAsync();
            var problem = await _flow.AllocateAsync(booking, await _flow.AllocationTravelersAsync(booking.Id), BookingFlowService.Stays(booking), config);
            if (problem != null) return BadRequest(Message(problem));
            booking.NeedsRoomReview = false;
            await _context.SaveChangesAsync();
            if (booking.DepartureDate != null) await _flow.RebalanceAsync(booking.PackageId, booking.DepartureDate, config);
            return NoContent();
        }

        /// <summary>
        /// Cancels a booking: seats and beds are freed and the rooms rebalanced. The refund is made
        /// from the MyFatoorah portal according to the cancellation terms.
        /// </summary>
        [HttpPost("bookings/{reference}/cancel")]
        public async Task<IActionResult> Cancel(string reference)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(item => item.Reference == reference);
            if (booking == null) return NotFound();
            if (booking.Status == BookingStatuses.Cancelled) return NoContent();
            await _flow.CancelAsync(booking);
            return NoContent();
        }

        /// <summary>Rooming list for the hotel and the supervisor (Excel).</summary>
        [HttpGet("rooming/export")]
        public async Task<IActionResult> Export([FromQuery, Required] string packageId, [FromQuery, Required] string departureDate)
        {
            var config = await _configStore.ReadAsync();
            var confirmed = _context.Bookings.Where(booking => booking.PackageId == packageId && booking.DepartureDate == departureDate && booking.Status == BookingStatuses.Confirmed);
            var rows = await _context.RoomAssignments.AsNoTracking()
                .Where(item => confirmed.Select(booking => booking.Id).Contains(item.BookingId))
                .Join(_context.Travelers.AsNoTracking(), item => item.TravelerId, traveler => traveler.Id, (item, traveler) => new { item, traveler })
                .Join(_context.Bookings.AsNoTracking(), pair => pair.item.BookingId, booking => booking.Id, (pair, booking) => new { pair.item, pair.traveler, booking.Reference })
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Rooming list");
            sheet.RightToLeft = true;
            var headers = new[] { "المدينة", "الفندق", "نوع الغرفة", "رقم الغرفة", "نوع السكن", "الاسم", "الجنس", "الجنسية", "رقم الجواز", "رقم الحجز" };
            for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
            var line = 2;
            foreach (var row in rows.OrderBy(row => row.item.City).ThenBy(row => row.item.HotelId).ThenBy(row => row.item.RoomType).ThenBy(row => row.item.RoomNumber))
            {
                sheet.Cell(line, 1).Value = BookingCities.Label(row.item.City);
                sheet.Cell(line, 2).Value = config.Hotel(row.item.HotelId)?.Name ?? row.item.HotelId;
                sheet.Cell(line, 3).Value = RoomLabels.Of(row.item.RoomType);
                sheet.Cell(line, 4).Value = row.item.RoomNumber;
                sheet.Cell(line, 5).Value = row.item.IsFamily ? "عائلة" : row.item.Gender == "female" ? "مشتركة - نساء" : "مشتركة - رجال";
                sheet.Cell(line, 6).Value = row.traveler.FullName;
                sheet.Cell(line, 7).Value = row.item.Gender == "female" ? "أنثى" : "ذكر";
                sheet.Cell(line, 8).Value = row.item.Nationality;
                sheet.Cell(line, 9).Value = row.traveler.PassportNumber;
                sheet.Cell(line, 10).Value = row.Reference;
                line++;
            }
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Columns().AdjustToContents();
            using var output = new MemoryStream();
            workbook.SaveAs(output);
            return File(output.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"rooming-{packageId}-{departureDate}.xlsx");
        }

        private IQueryable<RoomAssignment> RoomQuery(string packageId, string departureDate, string city, string hotelId, string roomType, int roomNumber) =>
            _context.RoomAssignments.Where(item => item.PackageId == packageId && item.DepartureDate == departureDate && item.City == city &&
                item.HotelId == hotelId && item.RoomType == roomType && item.RoomNumber == roomNumber);

        private static string? Validate(BookingConfig config)
        {
            if (config.VisaFees.Rowad < 0 || config.VisaFees.Existing < 0) return "رسوم التأشيرة لا تكون سالبة.";
            if (config.PaymentHoldMinutes is < 5 or > 120) return "مدة حجز المقاعد أثناء الدفع بين 5 و120 دقيقة.";
            if (config.MinPassportValidityMonths is < 0 or > 24) return "صلاحية الجواز الدنيا بين 0 و24 شهراً.";
            if (config.Nationalities.Count == 0) return "أضف جنسية واحدة على الأقل.";
            if (config.Nationalities.Select(item => item.Name.Trim()).Distinct().Count() != config.Nationalities.Count) return "جنسية مكرّرة.";
            var hotelIds = config.Hotels.Select(hotel => hotel.Id).ToList();
            if (hotelIds.Any(string.IsNullOrWhiteSpace) || hotelIds.Distinct().Count() != hotelIds.Count) return "لكل فندق معرّف فريد.";
            foreach (var hotel in config.Hotels)
            {
                if (string.IsNullOrWhiteSpace(hotel.Name)) return "اسم الفندق مطلوب.";
                if (hotel.City is not (BookingCities.Makkah or BookingCities.Madinah)) return $"مدينة الفندق {hotel.Name} غير صحيحة.";
                if (hotel.SupplementPerPersonPerNight < 0) return $"فرق سعر {hotel.Name} لا يكون سالباً.";
                if (hotel.Rooms.Count == 0) return $"أضف نوع غرفة واحداً على الأقل في {hotel.Name}.";
                foreach (var room in hotel.Rooms)
                {
                    if (room.Beds is < 1 or > 8) return $"عدد الأسرّة في {hotel.Name} بين 1 و8.";
                    if (room.RoomsPerDeparture < 0) return $"عدد الغرف في {hotel.Name} لا يكون سالباً.";
                    if (room.FamilySupplementPerRoomPerNight < 0 || room.SharedSupplementPerBedPerNight < 0) return $"فروق أسعار الغرف في {hotel.Name} لا تكون سالبة.";
                }
            }
            foreach (var pack in config.Packs)
            {
                if (pack.Nights.Makkah < 1 || pack.Nights.Madinah < 0) return "عدد الليالي غير صحيح.";
                if (pack.BusPrice < 0 || pack.AdultPrice < 0 || pack.ChildPrice < 0) return "الأسعار لا تكون سالبة.";
                if (pack.SeatsPerDeparture < 1) return "عدد المقاعد لكل موعد مطلوب.";
                foreach (var (city, ids) in pack.Hotels)
                {
                    if (ids.Any(id => config.Hotel(id)?.City != city)) return $"فندق غير موجود أو في مدينة أخرى ضمن باقة {pack.PackageId}.";
                }
                if (pack.Nights.Madinah > 0 && (!pack.Hotels.TryGetValue(BookingCities.Madinah, out var madinah) || madinah.Count == 0)) return $"باقة {pack.PackageId} تحتاج فندقاً في المدينة.";
                if (!pack.Hotels.TryGetValue(BookingCities.Makkah, out var makkah) || makkah.Count == 0) return $"باقة {pack.PackageId} تحتاج فندقاً في مكة.";
            }
            return null;
        }

        private static ProblemDetails Message(string text) => new() { Title = text, Detail = text };
    }

    public sealed class LockRequest
    {
        public bool Locked { get; set; }
    }

    public sealed class RoomLockRequest
    {
        [Required] public string PackageId { get; set; } = "";
        [Required] public string DepartureDate { get; set; } = "";
        [Required] public string City { get; set; } = "";
        [Required] public string HotelId { get; set; } = "";
        [Required] public string RoomType { get; set; } = "";
        public int RoomNumber { get; set; }
        public bool Locked { get; set; }
    }

    public sealed class MoveRequest
    {
        [Range(1, 500)] public int RoomNumber { get; set; }
        [StringLength(200)] public string? Reason { get; set; }
        public bool? Lock { get; set; }
    }

    public sealed class DepartureRequest
    {
        [Required] public string PackageId { get; set; } = "";
        [Required] public string DepartureDate { get; set; } = "";
    }
}
