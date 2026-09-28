using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public AdminController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        private async Task<bool> CanAccessDashboard()
        {
            return
                await _permissionService.HasPermissionAsync(User, "Dashboard.View") ||
                await _permissionService.HasPermissionAsync(User, "Travelers.View") ||
                await _permissionService.HasPermissionAsync(User, "Trips.View") ||
                await _permissionService.HasPermissionAsync(User, "Documents.View") ||
                await _permissionService.HasPermissionAsync(User, "Blocks.View") ||
                await _permissionService.HasPermissionAsync(User, "Reports.View") ||
                await _permissionService.HasPermissionAsync(User, "AuditLogs.View");
        }

        private async Task<bool> CanViewTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.View");
        }

        private async Task<bool> CanViewTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.View");
        }

        private async Task<bool> CanViewDocuments()
        {
            return await _permissionService.HasPermissionAsync(User, "Documents.View");
        }

        private async Task<bool> CanViewBlocks()
        {
            return await _permissionService.HasPermissionAsync(User, "Blocks.View");
        }

        private async Task<bool> CanViewReports()
        {
            return await _permissionService.HasPermissionAsync(User, "Reports.View");
        }

        private async Task<bool> CanViewAuditLogs()
        {
            return await _permissionService.HasPermissionAsync(User, "AuditLogs.View");
        }

        public async Task<IActionResult> Index()
        {
            if (!await CanAccessDashboard())
                return Forbid();

            var today = DateTime.Today;
            var sixMonthsLater = today.AddMonths(6);
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            var canViewTravelers = await CanViewTravelers();
            var canViewTrips = await CanViewTrips();
            var canViewDocuments = await CanViewDocuments();
            var canViewBlocks = await CanViewBlocks();
            var canViewReports = await CanViewReports();
            var canViewAuditLogs = await CanViewAuditLogs();

            ViewBag.CanViewTravelers = canViewTravelers;
            ViewBag.CanViewTrips = canViewTrips;
            ViewBag.CanViewDocuments = canViewDocuments;
            ViewBag.CanViewBlocks = canViewBlocks;
            ViewBag.CanViewReports = canViewReports;
            ViewBag.CanViewAuditLogs = canViewAuditLogs;

            ViewBag.TravelersCount = canViewTravelers
                ? await _context.Travelers.CountAsync(t => !t.IsDeleted)
                : 0;

            ViewBag.DeletedTravelersCount = canViewTravelers
                ? await _context.Travelers.CountAsync(t => t.IsDeleted)
                : 0;

            ViewBag.TripsCount = canViewTrips
                ? await _context.Trips.CountAsync(t => !t.IsDeleted && !t.Traveler.IsDeleted)
                : 0;

            ViewBag.BlockedCount = canViewBlocks
                ? await _context.Travelers.CountAsync(t => t.IsBlocked && !t.IsDeleted)
                : 0;

            ViewBag.DocumentsCount = canViewDocuments
                ? await _context.TravelerDocuments.CountAsync(d => !d.IsDeleted)
                : 0;

            ViewBag.DeletedDocumentsCount = canViewDocuments
                ? await _context.TravelerDocuments.CountAsync(d => d.IsDeleted)
                : 0;

            ViewBag.TodayAuditCount = canViewAuditLogs
                ? await _context.AuditLogs.CountAsync(a => a.CreatedAt.Date == today)
                : 0;

            ViewBag.WeekAuditCount = canViewAuditLogs
                ? await _context.AuditLogs.CountAsync(a => a.CreatedAt.Date >= startOfWeek)
                : 0;

            ViewBag.MonthAuditCount = canViewAuditLogs
                ? await _context.AuditLogs.CountAsync(a => a.CreatedAt.Date >= startOfMonth)
                : 0;

            ViewBag.TopEmployee = canViewAuditLogs
                ? await _context.AuditLogs
                    .GroupBy(a => a.EmployeeName)
                    .Select(g => new
                    {
                        EmployeeName = g.Key,
                        Count = g.Count()
                    })
                    .OrderByDescending(x => x.Count)
                    .FirstOrDefaultAsync()
                : null;

            ViewBag.RecentTravelersCount = canViewTravelers
                ? await _context.Travelers.CountAsync(t => !t.IsDeleted && t.CreatedAt.Date >= startOfMonth)
                : 0;

            ViewBag.UploadedDocumentsThisMonth = canViewDocuments
                ? await _context.TravelerDocuments.CountAsync(d => !d.IsDeleted && d.UploadedAt.Date >= startOfMonth)
                : 0;

            ViewBag.ExpiringPassportsCount = canViewTravelers
                ? await _context.Travelers.CountAsync(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate.Value.Date >= today &&
                    t.PassportExpiryDate.Value.Date <= sixMonthsLater)
                : 0;

            ViewBag.ExpiredPassportsCount = canViewTravelers
                ? await _context.Travelers.CountAsync(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate.Value.Date < today)
                : 0;

            ViewBag.ExpiringPassports = canViewTravelers
                ? await _context.Travelers
                    .Where(t =>
                        !t.IsDeleted &&
                        t.PassportExpiryDate.HasValue &&
                        t.PassportExpiryDate.Value.Date >= today &&
                        t.PassportExpiryDate.Value.Date <= sixMonthsLater)
                    .OrderBy(t => t.PassportExpiryDate)
                    .Take(10)
                    .ToListAsync()
                : new List<Traveler>();

            ViewBag.LatestAuditLogs = canViewAuditLogs
                ? await _context.AuditLogs
                    .OrderByDescending(a => a.CreatedAt)
                    .Take(10)
                    .ToListAsync()
                : new List<AuditLog>();

            return View();
        }
    }
}
