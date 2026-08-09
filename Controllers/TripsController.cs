using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Hubs;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.Services.PdfReports;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class TripsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;
        private readonly IPdfReportService _pdfReportService;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly AuditService _auditService;
        private readonly NotificationService _notificationService;

        public TripsController(
            ApplicationDbContext context,
            PermissionService permissionService,
            IPdfReportService pdfReportService,
            IHubContext<DashboardHub> hubContext,
            AuditService auditService,
            NotificationService notificationService)
        {
            _context = context;
            _permissionService = permissionService;
            _pdfReportService = pdfReportService;
            _hubContext = hubContext;
            _auditService = auditService;
            _notificationService = notificationService;
        }

        private async Task<bool> CanViewTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.View");
        }

        private async Task<bool> CanCreateTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.Create");
        }

        private async Task<bool> CanArchiveTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.Archive");
        }

        private async Task<bool> CanRestoreTrips()
        {
            return await _permissionService.HasPermissionAsync(User, "Trips.Restore");
        }

        private async Task<bool> CanExportReports()
        {
            return await _permissionService.HasPermissionAsync(User, "Reports.Export");
        }

        public async Task<IActionResult> Index()
        {
            if (!await CanViewTrips())
                return Forbid();

            var trips = await _context.Trips
                .AsNoTracking()
                .Include(t => t.Traveler)
                .Where(t => !t.IsDeleted && !t.Traveler.IsDeleted)
                .OrderByDescending(t => t.TripDate)
                .ToListAsync();

            ViewBag.CanArchiveTrips = await CanArchiveTrips();

            return View(trips);
        }

        public async Task<IActionResult> Deleted()
        {
            if (!await CanRestoreTrips())
                return Forbid();

            var trips = await _context.Trips
                .AsNoTracking()
                .Include(t => t.Traveler)
                .Where(t => t.IsDeleted)
                .OrderByDescending(t => t.DeletedAt)
                .ToListAsync();

            return View(trips);
        }

        public async Task<IActionResult> ExportToPdf()
        {
            if (!await CanExportReports())
                return Forbid();

            var trips = await _context.Trips
                .AsNoTracking()
                .Include(t => t.Traveler)
                .Where(t => !t.IsDeleted && !t.Traveler.IsDeleted)
                .OrderByDescending(t => t.TripDate)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateTripsReport(trips);

            return File(pdf, "application/pdf", $"Trips_{DateTime.Now:yyyyMMdd_HHmm}.pdf");
        }

        public async Task<IActionResult> ExportToExcel()
        {
            if (!await CanExportReports())
                return Forbid();

            var trips = await _context.Trips
                .AsNoTracking()
                .Include(t => t.Traveler)
                .Where(t => !t.IsDeleted && !t.Traveler.IsDeleted)
                .OrderByDescending(t => t.TripDate)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Trips");

            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "المسافر";
            worksheet.Cell(1, 3).Value = "رقم الجواز";
            worksheet.Cell(1, 4).Value = "نوع الرحلة";
            worksheet.Cell(1, 5).Value = "تاريخ الرحلة";
            worksheet.Cell(1, 6).Value = "ملاحظات";

            int row = 2;

            foreach (var trip in trips)
            {
                worksheet.Cell(row, 1).Value = trip.Id;
                worksheet.Cell(row, 2).Value = trip.Traveler?.FullName;
                worksheet.Cell(row, 3).Value = trip.Traveler?.PassportNumber;
                worksheet.Cell(row, 4).Value = trip.TripType;
                worksheet.Cell(row, 5).Value = trip.TripDate.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 6).Value = trip.Notes;
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Trips_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            );
        }

        public async Task<IActionResult> Create(int travelerId)
        {
            if (!await CanCreateTrips())
                return Forbid();

            var traveler = await _context.Travelers
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == travelerId && !t.IsDeleted);

            if (traveler == null)
                return NotFound();

            if (traveler.IsBlocked)
            {
                TempData["Error"] = "لا يمكن إضافة رحلة لمسافر محظور.";
                return RedirectToAction("Details", "Travelers", new { id = travelerId });
            }

            ViewBag.TravelerName = traveler.FullName;
            ViewBag.TravelerId = traveler.Id;

            return View(new Trip
            {
                TravelerId = traveler.Id,
                TripType = "Umrah",
                TripDate = DateTime.Today
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Trip trip)
        {
            if (!await CanCreateTrips())
                return Forbid();

            ModelState.Remove("Traveler");
            ModelState.Remove("TripType");

            var traveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.Id == trip.TravelerId && !t.IsDeleted);

            if (traveler == null)
                return NotFound();

            if (traveler.IsBlocked)
            {
                TempData["Error"] = "لا يمكن إضافة رحلة لمسافر محظور.";
                return RedirectToAction("Details", "Travelers", new { id = traveler.Id });
            }

            if (trip.TripDate == default)
                ModelState.AddModelError("TripDate", "الرجاء إدخال تاريخ الرحلة.");

            if (trip.TripDate.Date < DateTime.Today.AddYears(-1))
                ModelState.AddModelError("TripDate", "تاريخ الرحلة قديم جداً وغير مقبول.");

            if (ModelState.IsValid)
            {
                bool duplicateTrip = await _context.Trips.AnyAsync(t =>
                    !t.IsDeleted &&
                    t.TravelerId == traveler.Id &&
                    t.TripDate.Date == trip.TripDate.Date &&
                    t.TripType == "Umrah");

                if (duplicateTrip)
                {
                    ModelState.AddModelError("", "هذا المسافر لديه رحلة مسجلة بنفس التاريخ.");
                    ViewBag.TravelerName = traveler.FullName;
                    ViewBag.TravelerId = traveler.Id;
                    return View(trip);
                }

                trip.TripType = "Umrah";
                trip.CreatedAt = DateTime.UtcNow;
                trip.IsDeleted = false;

                _context.Trips.Add(trip);
                await _context.SaveChangesAsync();

                traveler.UmrahCount = await _context.Trips
                    .CountAsync(t => t.TravelerId == traveler.Id && !t.IsDeleted);

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    "CreateTrip",
                    "Trip",
                    trip.Id,
                    traveler.PassportNumber,
                    traveler.FullName,
                    $"تمت إضافة رحلة عمرة بتاريخ {trip.TripDate:yyyy-MM-dd}"
                );

                await _notificationService.NotifyTripsAsync(
                    "رحلة جديدة",
                    "تم تسجيل رحلة عمرة جديدة في النظام",
                    "success"
                );

                await NotifyDashboardUpdate();

                TempData["Success"] = "تمت إضافة الرحلة بنجاح.";

                return RedirectToAction("Details", "Travelers", new { id = traveler.Id });
            }

            ViewBag.TravelerName = traveler.FullName;
            ViewBag.TravelerId = traveler.Id;

            return View(trip);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await CanArchiveTrips())
                return Forbid();

            var trip = await _context.Trips
                .Include(t => t.Traveler)
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (trip == null)
                return NotFound();

            trip.IsDeleted = true;
            trip.DeletedAt = DateTime.UtcNow;
            trip.DeletedBy = User.Identity?.Name ?? "Unknown";

            await _context.SaveChangesAsync();

            if (trip.Traveler != null)
            {
                trip.Traveler.UmrahCount = await _context.Trips
                    .CountAsync(t => t.TravelerId == trip.TravelerId && !t.IsDeleted);

                await _context.SaveChangesAsync();
            }

            await _auditService.LogAsync(
                "ArchiveTrip",
                "Trip",
                trip.Id,
                trip.Traveler?.PassportNumber,
                trip.Traveler?.FullName,
                $"تمت أرشفة رحلة بتاريخ {trip.TripDate:yyyy-MM-dd}"
            );

            await _notificationService.NotifyTripsAsync(
                "أرشفة رحلة",
                "تم نقل رحلة إلى الأرشيف",
                "warning"
            );

            await NotifyDashboardUpdate();

            TempData["Warning"] = "تمت أرشفة الرحلة بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            if (!await CanRestoreTrips())
                return Forbid();

            var trip = await _context.Trips
                .Include(t => t.Traveler)
                .FirstOrDefaultAsync(t => t.Id == id && t.IsDeleted);

            if (trip == null)
                return NotFound();

            if (trip.Traveler == null || trip.Traveler.IsDeleted)
            {
                TempData["Error"] = "لا يمكن استرجاع رحلة لمسافر مؤرشف.";
                return RedirectToAction(nameof(Deleted));
            }

            trip.IsDeleted = false;
            trip.DeletedAt = null;
            trip.DeletedBy = null;

            await _context.SaveChangesAsync();

            trip.Traveler.UmrahCount = await _context.Trips
                .CountAsync(t => t.TravelerId == trip.TravelerId && !t.IsDeleted);

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "RestoreTrip",
                "Trip",
                trip.Id,
                trip.Traveler?.PassportNumber,
                trip.Traveler?.FullName,
                $"تم استرجاع رحلة بتاريخ {trip.TripDate:yyyy-MM-dd}"
            );

            await _notificationService.NotifyTripsAsync(
                "استرجاع رحلة",
                "تم استرجاع رحلة من الأرشيف",
                "success"
            );

            await NotifyDashboardUpdate();

            TempData["Success"] = "تم استرجاع الرحلة بنجاح.";

            return RedirectToAction(nameof(Deleted));
        }

        private async Task NotifyDashboardUpdate()
        {
            var today = DateTime.Today;
            var sixMonthsLater = today.AddMonths(6);

            var travelersCount = await _context.Travelers
                .CountAsync(t => !t.IsDeleted);

            var tripsCount = await _context.Trips
                .CountAsync(t => !t.IsDeleted && !t.Traveler.IsDeleted);

            var blockedCount = await _context.Travelers
                .CountAsync(t => t.IsBlocked && !t.IsDeleted);

            var expiringPassportsCount = await _context.Travelers
                .CountAsync(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate.Value.Date >= today &&
                    t.PassportExpiryDate.Value.Date <= sixMonthsLater);

            await _hubContext.Clients.All.SendAsync(
                "DashboardUpdated",
                new
                {
                    travelersCount,
                    tripsCount,
                    blockedCount,
                    expiringPassportsCount
                });
        }
    }
}