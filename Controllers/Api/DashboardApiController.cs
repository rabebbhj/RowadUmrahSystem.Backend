using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/dashboard")]
    public class DashboardApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public DashboardApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<DashboardDto>> Get()
        {
            var canViewTravelers = await HasPermission("Travelers.View");
            var canViewTrips = await HasPermission("Trips.View");
            var canViewDocuments = await HasPermission("Documents.View");
            var canViewBlocks = await HasPermission("Blocks.View");
            var canViewReports = await HasPermission("Reports.View");
            var canViewAuditLogs = await HasPermission("AuditLogs.View");

            if (!canViewTravelers &&
                !canViewTrips &&
                !canViewDocuments &&
                !canViewBlocks &&
                !canViewReports &&
                !canViewAuditLogs)
            {
                return Forbid();
            }

            var today = DateTime.Today;
            var tomorrow = today.AddDays(1);
            var sixMonthsLater = today.AddMonths(6);
            var sixMonthsLaterEnd = sixMonthsLater.AddDays(1);
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            TopEmployeeDto? topEmployee = null;
            if (canViewAuditLogs)
            {
                var employeeNames = await _context.AuditLogs
                    .AsNoTracking()
                    .Select(a => a.EmployeeName)
                    .ToListAsync();

                topEmployee = employeeNames
                    .GroupBy(name => string.IsNullOrWhiteSpace(name) ? "Unknown" : name)
                    .Select(group => new TopEmployeeDto(group.Key, group.Count()))
                    .OrderByDescending(item => item.Count)
                    .FirstOrDefault();
            }

            var expiringPassports = canViewTravelers
                ? await _context.Travelers
                    .AsNoTracking()
                    .Where(t =>
                        !t.IsDeleted &&
                        t.PassportExpiryDate.HasValue &&
                        t.PassportExpiryDate.Value >= today &&
                        t.PassportExpiryDate.Value < sixMonthsLaterEnd)
                    .OrderBy(t => t.PassportExpiryDate)
                    .Take(10)
                    .Select(t => new ExpiringPassportDto(
                        t.Id,
                        t.FullName,
                        t.PassportNumber,
                        t.Nationality,
                        t.PassportExpiryDate))
                    .ToListAsync()
                : new List<ExpiringPassportDto>();

            var latestAuditLogs = canViewAuditLogs
                ? await _context.AuditLogs
                    .AsNoTracking()
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(10)
                    .Select(a => new LatestAuditLogDto(
                        a.Action ?? string.Empty,
                        a.EmployeeName ?? string.Empty,
                        a.TravelerName,
                        a.Details ?? string.Empty,
                        a.CreatedAt))
                    .ToListAsync()
                : new List<LatestAuditLogDto>();

            return Ok(new DashboardDto(
                new DashboardPermissionsDto(
                    canViewTravelers,
                    canViewTrips,
                    canViewDocuments,
                    canViewBlocks,
                    canViewReports,
                    canViewAuditLogs),
                canViewTravelers ? await _context.Travelers.CountAsync(t => !t.IsDeleted) : 0,
                canViewTravelers ? await _context.Travelers.CountAsync(t => t.IsDeleted) : 0,
                canViewTrips ? await _context.Trips.CountAsync(t => !t.IsDeleted && !t.Traveler.IsDeleted) : 0,
                canViewBlocks ? await _context.Travelers.CountAsync(t => t.IsBlocked && !t.IsDeleted) : 0,
                canViewDocuments ? await _context.TravelerDocuments.CountAsync(d => !d.IsDeleted) : 0,
                canViewDocuments ? await _context.TravelerDocuments.CountAsync(d => d.IsDeleted) : 0,
                canViewAuditLogs ? await _context.AuditLogs.CountAsync(a => a.CreatedAt >= today && a.CreatedAt < tomorrow) : 0,
                canViewAuditLogs ? await _context.AuditLogs.CountAsync(a => a.CreatedAt >= startOfWeek) : 0,
                canViewAuditLogs ? await _context.AuditLogs.CountAsync(a => a.CreatedAt >= startOfMonth) : 0,
                topEmployee,
                canViewTravelers ? await _context.Travelers.CountAsync(t => !t.IsDeleted && t.CreatedAt >= startOfMonth) : 0,
                canViewDocuments ? await _context.TravelerDocuments.CountAsync(d => !d.IsDeleted && d.UploadedAt >= startOfMonth) : 0,
                canViewTravelers
                    ? await _context.Travelers.CountAsync(t =>
                        !t.IsDeleted &&
                        t.PassportExpiryDate.HasValue &&
                        t.PassportExpiryDate.Value >= today &&
                        t.PassportExpiryDate.Value < sixMonthsLaterEnd)
                    : 0,
                canViewTravelers
                    ? await _context.Travelers.CountAsync(t =>
                        !t.IsDeleted &&
                        t.PassportExpiryDate.HasValue &&
                        t.PassportExpiryDate.Value < today)
                    : 0,
                expiringPassports,
                latestAuditLogs));
        }

        private async Task<bool> HasPermission(string permissionName)
        {
            return await _permissionService.HasPermissionAsync(User, permissionName);
        }

        public sealed record DashboardPermissionsDto(
            bool CanViewTravelers,
            bool CanViewTrips,
            bool CanViewDocuments,
            bool CanViewBlocks,
            bool CanViewReports,
            bool CanViewAuditLogs);

        public sealed record TopEmployeeDto(string EmployeeName, int Count);

        public sealed record ExpiringPassportDto(
            int Id,
            string FullName,
            string PassportNumber,
            string Nationality,
            DateTime? PassportExpiryDate);

        public sealed record LatestAuditLogDto(
            string Action,
            string EmployeeName,
            string? TravelerName,
            string Details,
            DateTime CreatedAt);

        public sealed record DashboardDto(
            DashboardPermissionsDto Permissions,
            int TravelersCount,
            int DeletedTravelersCount,
            int TripsCount,
            int BlockedCount,
            int DocumentsCount,
            int DeletedDocumentsCount,
            int TodayAuditCount,
            int WeekAuditCount,
            int MonthAuditCount,
            TopEmployeeDto? TopEmployee,
            int RecentTravelersCount,
            int UploadedDocumentsThisMonth,
            int ExpiringPassportsCount,
            int ExpiredPassportsCount,
            IReadOnlyList<ExpiringPassportDto> ExpiringPassports,
            IReadOnlyList<LatestAuditLogDto> LatestAuditLogs);
    }
}
