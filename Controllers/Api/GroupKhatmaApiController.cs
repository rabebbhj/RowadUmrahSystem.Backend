using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    /// <summary>
    /// "ختمة المجموعة": a Quran khatma shared by the pilgrims of one trip (same package and
    /// departure date). Members take free hizbs and mark them read; the last one completes the
    /// khatma and notifies the whole group.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/group-khatma")]
    public class GroupKhatmaApiController : ControllerBase
    {
        private const int HizbCount = 60;
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public GroupKhatmaApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet("mine")]
        public async Task<ActionResult<GroupKhatmaStateDto>> GetMine()
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();
            var trip = await FindTripAsync(userId);
            if (trip == null) return Ok(new GroupKhatmaStateDto(null, null));
            return Ok(await BuildStateAsync(trip, userId));
        }

        [HttpPost]
        public async Task<ActionResult<GroupKhatmaStateDto>> Create([FromBody] CreateGroupKhatmaRequest request)
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();
            var trip = await FindTripAsync(userId);
            if (trip == null) return BadRequest(Error("لا توجد رحلة مرتبطة بحسابك لبدء ختمة جماعية."));

            if (await OpenKhatmaQuery(trip).AnyAsync()) return Ok(await BuildStateAsync(trip, userId));

            var khatma = new GroupKhatma
            {
                PackageId = trip.PackageId,
                DepartureDate = trip.DepartureDate,
                Dedication = string.IsNullOrWhiteSpace(request.Dedication) ? null : request.Dedication.Trim(),
                CreatedByUserId = userId,
                Hizbs = Enumerable.Range(1, HizbCount).Select(hizb => new GroupKhatmaHizb { Hizb = hizb }).ToList()
            };
            _context.GroupKhatmas.Add(khatma);
            await _context.SaveChangesAsync();
            return Ok(await BuildStateAsync(trip, userId));
        }

        /// <summary>Reserves the first free hizb for the caller.</summary>
        [HttpPost("take")]
        public async Task<ActionResult<GroupKhatmaStateDto>> TakeNext()
        {
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();
            var trip = await FindTripAsync(userId);
            var khatma = trip == null ? null : await OpenKhatmaQuery(trip).FirstOrDefaultAsync();
            if (trip == null || khatma == null) return NotFound(Error("لا توجد ختمة جماعية جارية."));

            // Conditional update: two members tapping together never get the same hizb.
            var candidates = await _context.GroupKhatmaHizbs
                .Where(item => item.GroupKhatmaId == khatma.Id && item.Status == GroupKhatmaHizbStatuses.Free)
                .OrderBy(item => item.Hizb).Select(item => item.Id).Take(5).ToListAsync();
            foreach (var id in candidates)
            {
                var updated = await _context.GroupKhatmaHizbs
                    .Where(item => item.Id == id && item.Status == GroupKhatmaHizbStatuses.Free)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(item => item.Status, GroupKhatmaHizbStatuses.Reserved)
                        .SetProperty(item => item.UserId, userId)
                        .SetProperty(item => item.ReservedAt, DateTime.UtcNow));
                if (updated == 1) return Ok(await BuildStateAsync(trip, userId));
            }
            return Conflict(Error("لا توجد أحزاب متاحة حالياً."));
        }

        /// <summary>Marks a hizb read: one the caller reserved, or a free one (taken and read at once).</summary>
        [HttpPost("hizbs/{hizb:int}/read")]
        public async Task<ActionResult<GroupKhatmaStateDto>> MarkRead(int hizb)
        {
            if (hizb < 1 || hizb > HizbCount) return BadRequest(Error("رقم الحزب غير صحيح."));
            var userId = _userManager.GetUserId(User);
            if (userId == null) return Unauthorized();
            var trip = await FindTripAsync(userId);
            var khatma = trip == null ? null : await OpenKhatmaQuery(trip).FirstOrDefaultAsync();
            if (trip == null || khatma == null) return NotFound(Error("لا توجد ختمة جماعية جارية."));

            var row = await _context.GroupKhatmaHizbs.FirstAsync(item => item.GroupKhatmaId == khatma.Id && item.Hizb == hizb);
            if (row.Status == GroupKhatmaHizbStatuses.Read) return Ok(await BuildStateAsync(trip, userId));
            if (row.Status == GroupKhatmaHizbStatuses.Reserved && row.UserId != userId) return Conflict(Error("هذا الحزب محجوز لعضو آخر في المجموعة."));

            var updated = await _context.GroupKhatmaHizbs
                .Where(item => item.Id == row.Id && item.Status != GroupKhatmaHizbStatuses.Read && (item.UserId == null || item.UserId == userId))
                .ExecuteUpdateAsync(set => set
                    .SetProperty(item => item.Status, GroupKhatmaHizbStatuses.Read)
                    .SetProperty(item => item.UserId, userId)
                    .SetProperty(item => item.ReadAt, DateTime.UtcNow));
            if (updated == 0) return Conflict(Error("تغيّرت حالة هذا الحزب، حدّث الصفحة."));

            await CompleteIfFinishedAsync(khatma, trip);
            return Ok(await BuildStateAsync(trip, userId));
        }

        private async Task CompleteIfFinishedAsync(GroupKhatma khatma, TripGroup trip)
        {
            var read = await _context.GroupKhatmaHizbs.CountAsync(item => item.GroupKhatmaId == khatma.Id && item.Status == GroupKhatmaHizbStatuses.Read);
            if (read < HizbCount) return;
            var completed = await _context.GroupKhatmas
                .Where(item => item.Id == khatma.Id && item.CompletedAt == null)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.CompletedAt, DateTime.UtcNow));
            if (completed == 0) return;

            var notification = new Notification
            {
                Title = "ختمة المجموعة",
                Message = "ختمت مجموعتكم القرآن، تقبّل الله",
                Type = "group-khatma",
                TargetGroup = $"trip:{trip.PackageId}:{trip.DepartureDate}"
            };
            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
            _context.UserNotifications.AddRange(trip.UserIds.Select(id => new UserNotification { UserId = id, NotificationId = notification.Id }));
            await _context.SaveChangesAsync();
        }

        private IQueryable<GroupKhatma> OpenKhatmaQuery(TripGroup trip) => _context.GroupKhatmas
            .Where(item => item.PackageId == trip.PackageId && item.DepartureDate == trip.DepartureDate && item.CompletedAt == null);

        /// <summary>The caller's current or next trip (booking with a date), else the latest one.</summary>
        private async Task<TripGroup?> FindTripAsync(string userId)
        {
            var bookings = await _context.Bookings.AsNoTracking()
                .Where(item => item.UserId == userId && item.Status != BookingStatuses.Cancelled && item.DepartureDate != null)
                .Select(item => new { item.PackageId, item.PackageName, item.DepartureDate, item.DurationDays })
                .ToListAsync();
            if (bookings.Count == 0) return null;
            var today = DateTime.UtcNow.AddHours(3).Date;
            var chosen = bookings
                .Where(item => DateTime.TryParse(item.DepartureDate, out var departure) && departure.AddDays(item.DurationDays) >= today)
                .OrderBy(item => item.DepartureDate)
                .FirstOrDefault() ?? bookings.OrderByDescending(item => item.DepartureDate).First();

            var groupBookings = await _context.Bookings.AsNoTracking()
                .Where(item => item.PackageId == chosen.PackageId && item.DepartureDate == chosen.DepartureDate && item.Status != BookingStatuses.Cancelled)
                .Include(item => item.Travelers).ThenInclude(link => link.Traveler)
                .ToListAsync();
            return new TripGroup(
                chosen.PackageId,
                chosen.DepartureDate!,
                chosen.PackageName,
                groupBookings.Sum(item => item.Adults + item.Children + item.Infants),
                groupBookings.Select(item => item.UserId).Distinct().ToList(),
                groupBookings.SelectMany(item => item.Travelers.Select(link => link.Traveler.FullName)).ToList());
        }

        private async Task<GroupKhatmaStateDto> BuildStateAsync(TripGroup trip, string userId)
        {
            var khatma = await _context.GroupKhatmas.AsNoTracking()
                .Include(item => item.Hizbs)
                .Where(item => item.PackageId == trip.PackageId && item.DepartureDate == trip.DepartureDate)
                .OrderByDescending(item => item.CompletedAt == null).ThenByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync();
            // Members are shown by initials only.
            var members = trip.TravelerNames.Select((name, index) => new GroupMemberDto(index + 1, Initials(name))).ToList();
            var group = new GroupDto(trip.PackageId, trip.DepartureDate, $"مجموعة {trip.PackageName}", trip.TravelerCount, members);
            if (khatma == null) return new GroupKhatmaStateDto(group, null);
            var hizbs = khatma.Hizbs.OrderBy(item => item.Hizb)
                .Select(item => new GroupHizbDto(item.Hizb, item.Status, item.UserId == userId))
                .ToList();
            return new GroupKhatmaStateDto(group, new GroupKhatmaDto(khatma.Id, khatma.Dedication, khatma.CreatedAt, khatma.CompletedAt, hizbs));
        }

        private static string Initials(string? name)
        {
            var words = (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return "؟";
            return words.Length == 1 ? words[0][..1] : $"{words[0][0]}{words[^1][0]}";
        }

        private static ProblemDetails Error(string message) => new() { Title = message, Detail = message };

        private sealed record TripGroup(string PackageId, string DepartureDate, string PackageName, int TravelerCount, List<string> UserIds, List<string> TravelerNames);
    }

    public sealed class CreateGroupKhatmaRequest
    {
        [StringLength(120)] public string? Dedication { get; set; }
    }

    public sealed record GroupMemberDto(int Id, string Initials);
    public sealed record GroupDto(string PackageId, string DepartureDate, string Name, int MemberCount, IReadOnlyList<GroupMemberDto> Members);
    public sealed record GroupHizbDto(int Hizb, string State, bool Mine);
    public sealed record GroupKhatmaDto(int Id, string? Dedication, DateTime CreatedAt, DateTime? CompletedAt, IReadOnlyList<GroupHizbDto> Hizbs);
    public sealed record GroupKhatmaStateDto(GroupDto? Group, GroupKhatmaDto? Khatma);
}
