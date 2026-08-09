using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.Services.PdfReports;
using Microsoft.AspNetCore.SignalR;
using RowadUmrahSystem.Web.Hubs;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class TravelersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PermissionService _permissionService;
        private readonly PassportOcrService _passportOcrService;
        private readonly AuditService _auditService;
        private readonly IPdfReportService _pdfReportService;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly NotificationService _notificationService;
        public TravelersController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            PermissionService permissionService,
            PassportOcrService passportOcrService,
            AuditService auditService,
            IPdfReportService pdfReportService,
            IHubContext<DashboardHub> hubContext,
            NotificationService notificationService)
        {
            _context = context;
            _environment = environment;
            _permissionService = permissionService;
            _passportOcrService = passportOcrService;
            _auditService = auditService;
            _pdfReportService = pdfReportService;
            _hubContext = hubContext;
            _notificationService = notificationService;
        }

        private async Task<bool> CanAccessTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.View");
        }

        private async Task<bool> CanCreateTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.Create");
        }

        private async Task<bool> CanEditTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.Edit");
        }

        private async Task<bool> CanArchiveTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.Archive");
        }

        private async Task<bool> CanRestoreTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Travelers.Restore");
        }

        private async Task<bool> CanAccessBlocks()
        {
            return await _permissionService.HasPermissionAsync(User, "Blocks.View");
        }

        private async Task<bool> CanBlockTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Blocks.Block");
        }

        private async Task<bool> CanUnblockTravelers()
        {
            return await _permissionService.HasPermissionAsync(User, "Blocks.Unblock");
        }

        private async Task<bool> CanExportReports()
        {
            return await _permissionService.HasPermissionAsync(User, "Reports.Export");
        }
        public async Task<IActionResult> Index(string? search)
        {
            if (!await CanAccessTravelers())
                return Forbid();

            var travelers = _context.Travelers
    .AsNoTracking()
    .Where(t => !t.IsDeleted)
    .Include(t => t.Trips)
    .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                travelers = travelers.Where(t =>
                    t.PassportNumber.Contains(search) ||
                    t.FullName.Contains(search) ||
                    t.PhoneNumber.Contains(search));
            }

            ViewBag.Search = search;

            return View(await travelers.ToListAsync());
        }

        public async Task<IActionResult> ExportToPdf()
        {
            if (!await CanExportReports())
                return Forbid();

            var travelers = await _context.Travelers
                .Where(t => !t.IsDeleted)
                .OrderBy(t => t.FullName)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateTravelersReport(travelers);

            return File(pdf, "application/pdf", $"Travelers_{DateTime.Now:yyyyMMdd}.pdf");
        }

        public async Task<IActionResult> ExportDeletedToPdf()
        {
            if (!await CanExportReports())
                return Forbid();
            var travelers = await _context.Travelers
                .Where(t => t.IsDeleted)
                .OrderByDescending(t => t.DeletedAt)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateDeletedTravelersReport(travelers);

            return File(pdf, "application/pdf", $"DeletedTravelers_{DateTime.Now:yyyyMMdd}.pdf");
        }

        public async Task<IActionResult> ExportExpiringPassportsPdf()
        {
            if (!await CanExportReports())
                return Forbid();

            var today = DateTime.Today;
            var sixMonthsLater = today.AddMonths(6);

            var travelers = await _context.Travelers
                .Where(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate.Value.Date >= today &&
                    t.PassportExpiryDate.Value.Date <= sixMonthsLater)
                .OrderBy(t => t.PassportExpiryDate)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateExpiringPassportsReport(travelers);

            return File(pdf, "application/pdf", $"ExpiringPassports_{DateTime.Now:yyyyMMdd}.pdf");
        }

        public async Task<IActionResult> ExportExpiredPassportsPdf()
        {
            if (!await CanExportReports())
                return Forbid();

            var today = DateTime.Today;

            var travelers = await _context.Travelers
                .Where(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate.Value.Date < today)
                .OrderBy(t => t.PassportExpiryDate)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateExpiredPassportsReport(travelers);

            return File(pdf, "application/pdf", $"ExpiredPassports_{DateTime.Now:yyyyMMdd}.pdf");
        }


        public async Task<IActionResult> BlockedPdf()
        {
            if (!await CanExportReports())
                return Forbid();
            var travelers = await _context.Travelers
                .Where(t => t.IsBlocked && !t.IsDeleted)
                .OrderByDescending(t => t.BlockedAt)
                .ToListAsync();

            var pdf = _pdfReportService
                .GenerateBlockedTravelersReport(travelers);

            return File(
                pdf,
                "application/pdf",
                $"BlockedTravelers_{DateTime.Now:yyyyMMdd}.pdf");
        }
        public async Task<IActionResult> TravelerPdf(int id)
        {
            if (!await CanExportReports())
                return Forbid();

            var traveler = await _context.Travelers
                .Include(t => t.Trips)
                .Include(t => t.Documents.Where(d => !d.IsDeleted))
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (traveler == null)
                return NotFound();

            var pdf = _pdfReportService
                .GenerateTravelerProfile(traveler);

            return File(
                pdf,
                "application/pdf",
                $"Traveler_{traveler.Id}.pdf");
        }

        public async Task<IActionResult> ExportDeletedToExcel()
        {
            if (!await CanExportReports())
                return Forbid();

            var travelers = await _context.Travelers
                .Where(t => t.IsDeleted)
                .OrderByDescending(t => t.DeletedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Deleted Travelers");

            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "الاسم";
            worksheet.Cell(1, 3).Value = "رقم الجواز";
            worksheet.Cell(1, 4).Value = "الجنسية";
            worksheet.Cell(1, 5).Value = "الهاتف";
            worksheet.Cell(1, 6).Value = "تاريخ الأرشفة";
            worksheet.Cell(1, 7).Value = "تمت بواسطة";

            int row = 2;

            foreach (var t in travelers)
            {
                worksheet.Cell(row, 1).Value = t.Id;
                worksheet.Cell(row, 2).Value = t.FullName;
                worksheet.Cell(row, 3).Value = t.PassportNumber;
                worksheet.Cell(row, 4).Value = t.Nationality;
                worksheet.Cell(row, 5).Value = t.PhoneNumber;
                worksheet.Cell(row, 6).Value = t.DeletedAt?.ToString("yyyy-MM-dd HH:mm");
                worksheet.Cell(row, 7).Value = t.DeletedBy;
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"DeletedTravelers_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            );
        }
        public async Task<IActionResult> ExportBlockedToExcel()
        {
            if (!await CanExportReports())
                return Forbid();

            var travelers = await _context.Travelers
                .Where(t => t.IsBlocked && !t.IsDeleted)
                .OrderByDescending(t => t.BlockedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Blocked Travelers");

            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "الاسم";
            worksheet.Cell(1, 3).Value = "رقم الجواز";
            worksheet.Cell(1, 4).Value = "الجنسية";
            worksheet.Cell(1, 5).Value = "الهاتف";
            worksheet.Cell(1, 6).Value = "سبب الحظر";
            worksheet.Cell(1, 7).Value = "تاريخ الحظر";

            int row = 2;

            foreach (var t in travelers)
            {
                worksheet.Cell(row, 1).Value = t.Id;
                worksheet.Cell(row, 2).Value = t.FullName;
                worksheet.Cell(row, 3).Value = t.PassportNumber;
                worksheet.Cell(row, 4).Value = t.Nationality;
                worksheet.Cell(row, 5).Value = t.PhoneNumber;
                worksheet.Cell(row, 6).Value = t.BlockReason;
                worksheet.Cell(row, 7).Value = t.BlockedAt?.ToString("yyyy-MM-dd HH:mm");
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"BlockedTravelers_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            );
        }
        public async Task<IActionResult> ExportExpiredPassports()
        {
            if (!await CanExportReports())
                return Forbid();

            var today = DateTime.Today;

            var travelers = await _context.Travelers
                .Where(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate.Value.Date < today)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Expired Passports");

            ws.Cell(1, 1).Value = "الاسم";
            ws.Cell(1, 2).Value = "رقم الجواز";
            ws.Cell(1, 3).Value = "الجنسية";
            ws.Cell(1, 4).Value = "تاريخ انتهاء الجواز";

            int row = 2;

            foreach (var t in travelers)
            {
                ws.Cell(row, 1).Value = t.FullName;
                ws.Cell(row, 2).Value = t.PassportNumber;
                ws.Cell(row, 3).Value = t.Nationality;
                ws.Cell(row, 4).Value = t.PassportExpiryDate?.ToString("yyyy-MM-dd");
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ExpiredPassports_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
        public async Task<IActionResult> ExportExpiringPassports()
        {
            if (!await CanExportReports())
                return Forbid();

            var today = DateTime.Today;
            var sixMonthsLater = today.AddMonths(6);

            var travelers = await _context.Travelers
                .Where(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate.Value.Date >= today &&
                    t.PassportExpiryDate.Value.Date <= sixMonthsLater)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Expiring Passports");

            ws.Cell(1, 1).Value = "الاسم";
            ws.Cell(1, 2).Value = "رقم الجواز";
            ws.Cell(1, 3).Value = "الجنسية";
            ws.Cell(1, 4).Value = "تاريخ انتهاء الجواز";

            int row = 2;

            foreach (var t in travelers)
            {
                ws.Cell(row, 1).Value = t.FullName;
                ws.Cell(row, 2).Value = t.PassportNumber;
                ws.Cell(row, 3).Value = t.Nationality;
                ws.Cell(row, 4).Value = t.PassportExpiryDate?.ToString("yyyy-MM-dd");
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ExpiringPassports_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }
        public async Task<IActionResult> Blocked()
        {
            if (!await CanAccessBlocks())
                return Forbid();

            var blockedTravelers = await _context.Travelers
                .Where(t => t.IsBlocked && !t.IsDeleted)
                .OrderByDescending(t => t.BlockedAt)
                .ToListAsync();

            return View(blockedTravelers);
        }

        public async Task<IActionResult> Create()
        {
            if (!await CanCreateTravelers())
                return Forbid();

            return View(new Traveler());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReadPassport(IFormFile? passportImage)
        {
            ModelState.Clear();

            if (!await CanCreateTravelers())
                return Forbid();

            if (passportImage == null || passportImage.Length == 0)
            {
                ModelState.AddModelError("", "الرجاء رفع صورة الجواز.");
                return View("Create", new Traveler());
            }

            var validation = _passportOcrService.ValidateImage(passportImage);

            if (!validation.IsValid)
            {
                ModelState.AddModelError("", validation.Message);
                return View("Create", new Traveler());
            }

            string uploadsFolder = Path.Combine(
                _environment.WebRootPath,
                "uploads",
                "passports");

            Directory.CreateDirectory(uploadsFolder);

            string fileName =
                Guid.NewGuid().ToString()
                + Path.GetExtension(passportImage.FileName);

            string filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await passportImage.CopyToAsync(stream);
            }

            string relativePath = "/uploads/passports/" + fileName;

            var ocrResult = await _passportOcrService.ReadPassportAsync(relativePath);

            var traveler = new Traveler
            {
                PassportImagePath = relativePath,
                PassportNumber = ocrResult.PassportNumber ?? string.Empty,
                FullName = ocrResult.FullName ?? string.Empty,
                Nationality = ocrResult.Nationality ?? string.Empty,
                Gender = ocrResult.Gender ?? string.Empty,
                PassportExpiryDate = ocrResult.PassportExpiryDate
            };

            if (ocrResult.DateOfBirth.HasValue)
            {
                traveler.DateOfBirth = ocrResult.DateOfBirth.Value;
            }

            ModelState.Clear();

            return View("Create", traveler);
        }

        public async Task<IActionResult> ExportToExcel()
        {
            if (!await CanExportReports())
                return Forbid();

            var travelers = await _context.Travelers
                .Where(t => !t.IsDeleted)
                .Include(t => t.Trips)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Travelers");

            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "الاسم الكامل";
            worksheet.Cell(1, 3).Value = "رقم الجواز";
            worksheet.Cell(1, 4).Value = "الجنسية";
            worksheet.Cell(1, 5).Value = "الجنس";
            worksheet.Cell(1, 6).Value = "تاريخ الميلاد";
            worksheet.Cell(1, 7).Value = "رقم الهاتف";
            worksheet.Cell(1, 8).Value = "الإيميل";
            worksheet.Cell(1, 9).Value = "تاريخ انتهاء الجواز";
            worksheet.Cell(1, 10).Value = "عدد مرات العمرة";
            worksheet.Cell(1, 11).Value = "الحالة";
            worksheet.Cell(1, 12).Value = "تاريخ التسجيل";

            int row = 2;

            foreach (var traveler in travelers)
            {
                worksheet.Cell(row, 1).Value = traveler.Id;
                worksheet.Cell(row, 2).Value = traveler.FullName;
                worksheet.Cell(row, 3).Value = traveler.PassportNumber;
                worksheet.Cell(row, 4).Value = traveler.Nationality;
                worksheet.Cell(row, 5).Value = traveler.Gender;
                worksheet.Cell(row, 6).Value = traveler.DateOfBirth.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 7).Value = traveler.PhoneNumber;
                worksheet.Cell(row, 8).Value = traveler.Email;
                worksheet.Cell(row, 9).Value = traveler.PassportExpiryDate?.ToString("yyyy-MM-dd");
                worksheet.Cell(row, 10).Value = traveler.Trips.Count;
                worksheet.Cell(row, 11).Value = traveler.IsBlocked ? "محظور" : "مسموح";
                worksheet.Cell(row, 12).Value = traveler.CreatedAt.ToString("yyyy-MM-dd HH:mm");

                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var content = stream.ToArray();

            return File(
                content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Travelers_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
     Traveler traveler,
     IFormFile? passportImage)
        {
            if (!await CanCreateTravelers())
                return Forbid();

            if (string.IsNullOrEmpty(traveler.PassportImagePath) &&
                (passportImage == null || passportImage.Length == 0))
            {
                ModelState.AddModelError("", "الرجاء رفع صورة الجواز أو قراءة بيانات الجواز أولاً.");
            }

            if (ModelState.IsValid)
            {
                var existingTraveler = await _context.Travelers
    .FirstOrDefaultAsync(t =>
        t.PassportNumber == traveler.PassportNumber &&
        !t.IsDeleted);

                if (existingTraveler != null)
                {
                    if (existingTraveler.IsBlocked)
                    {
                        ModelState.AddModelError(
                            "PassportNumber",
                            $"هذا المسافر محظور. سبب الحظر: {existingTraveler.BlockReason}"
                        );

                        return View(traveler);
                    }

                    ModelState.AddModelError(
                        "PassportNumber",
                        "هذا المسافر مسجل مسبقاً بنفس رقم الجواز."
                    );

                    return View(traveler);
                }

                if (string.IsNullOrEmpty(traveler.PassportImagePath) &&
                    passportImage != null &&
                    passportImage.Length > 0)
                {
                    string uploadsFolder = Path.Combine(
                        _environment.WebRootPath,
                        "uploads",
                        "passports");

                    Directory.CreateDirectory(uploadsFolder);

                    string fileName =
                        Guid.NewGuid().ToString()
                        + Path.GetExtension(passportImage.FileName);

                    string filePath =
                        Path.Combine(uploadsFolder, fileName);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await passportImage.CopyToAsync(stream);
                    }

                    traveler.PassportImagePath =
                        "/uploads/passports/" + fileName;
                }

                traveler.UmrahCount = 1;
                traveler.CreatedAt = DateTime.UtcNow;

                _context.Travelers.Add(traveler);
                await _context.SaveChangesAsync();
                await _auditService.LogAsync(
    "Add",
    "Traveler",
    traveler.Id,
    traveler.PassportNumber,
    traveler.FullName,
    "تم إنشاء مسافر جديد"
);
                await _notificationService.NotifyTravelersAsync(
    "مسافر جديد",
    "تم تسجيل مسافر جديد في النظام",
    "success"
);

                await NotifyDashboardUpdate();

                TempData["Success"] = "تم تسجيل المسافر بنجاح";
                return RedirectToAction(nameof(Index));
            }

            return View(traveler);
        }

        public async Task<IActionResult> Details(int id)
        {
            if (!await CanAccessTravelers())
                return Forbid();

            var traveler = await _context.Travelers
                .Include(t => t.Trips)
                .Include(t => t.Documents.Where(d => !d.IsDeleted))
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

            if (traveler == null)
                return NotFound();

            return View(traveler);
        }
        public async Task<IActionResult> Edit(int id)
        {
            if (!await CanEditTravelers())
                return Forbid();

            var traveler = await _context.Travelers
    .FirstOrDefaultAsync(t =>
        t.Id == id &&
        !t.IsDeleted);

            if (traveler == null)
                return NotFound();

            return View(traveler);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Traveler traveler)
        {
            if (!await CanEditTravelers())
                return Forbid();

            if (id != traveler.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                var oldTraveler = await _context.Travelers
    .AsNoTracking()
    .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted);

                if (oldTraveler == null)
                    return NotFound();

                var changes = BuildTravelerChanges(oldTraveler, traveler);

                _context.Update(traveler);
                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    "Edit",
                    "Traveler",
                    traveler.Id,
                    traveler.PassportNumber,
                    traveler.FullName,
                    changes
                );
                await _notificationService.NotifyTravelersAsync(
    "تعديل مسافر",
    "تم تعديل بيانات أحد المسافرين",
    "info"
);

                return RedirectToAction(nameof(Index));
            }

            return View(traveler);
        }

        public async Task<IActionResult> Delete(int id)
        {
            if (!await CanArchiveTravelers())
                return Forbid();

            var traveler = await _context.Travelers.FindAsync(id);

            if (traveler == null)
                return NotFound();

            return View(traveler);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!await CanArchiveTravelers())
                return Forbid();

            var traveler = await _context.Travelers.FindAsync(id);

            if (traveler != null)
            {
                traveler.IsDeleted = true;
                traveler.DeletedAt = DateTime.UtcNow;
                traveler.DeletedBy = User.Identity?.Name ?? "Unknown";

                await _context.SaveChangesAsync();

                await _auditService.LogAsync(
                    "SoftDelete",
                    "Traveler",
                    traveler.Id,
                    traveler.PassportNumber,
                    traveler.FullName,
                    "تم أرشفة المسافر بدلاً من حذفه نهائياً"
                );
                await _notificationService.NotifyTravelersAsync(
    "أرشفة مسافر",
    "تم نقل مسافر إلى الأرشيف",
    "warning"
);
                await NotifyDashboardUpdate();
            }

            return RedirectToAction(nameof(Index));
        }
        public async Task<IActionResult> Deleted()
        {
            if (!await CanRestoreTravelers())
                return Forbid();

            var deletedTravelers = await _context.Travelers
                .Where(t => t.IsDeleted)
                .OrderByDescending(t => t.DeletedAt)
                .ToListAsync();

            return View(deletedTravelers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            if (!await CanRestoreTravelers())
                return Forbid();

            var traveler = await _context.Travelers.FindAsync(id);

            if (traveler == null)
                return NotFound();

            traveler.IsDeleted = false;
            traveler.DeletedAt = null;
            traveler.DeletedBy = null;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "Restore",
                "Traveler",
                traveler.Id,
                traveler.PassportNumber,
                traveler.FullName,
                "تم استرجاع المسافر من الأرشيف"
            );
            await _notificationService.NotifyTravelersAsync(
    "استرجاع مسافر",
    "تم استرجاع مسافر من الأرشيف",
    "success"
);

            await NotifyDashboardUpdate();

            return RedirectToAction(nameof(Deleted));
        }

        public async Task<IActionResult> Block(int id)
        {
            if (!await CanBlockTravelers())
                return Forbid();

            var traveler = await _context.Travelers.FindAsync(id);

            if (traveler == null)
                return NotFound();

            return View(traveler);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Block(int id, string blockReason)
        {
            if (!await CanBlockTravelers())
                return Forbid();

            var traveler = await _context.Travelers.FindAsync(id);

            if (traveler == null)
                return NotFound();

            traveler.IsBlocked = true;
            traveler.BlockReason = blockReason;
            traveler.BlockedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
    "Block",
    "Traveler",
    traveler.Id,
    traveler.PassportNumber,
    traveler.FullName,
    $"تم حظر المسافر. السبب: {blockReason}"
);
            await _notificationService.NotifyBlocksAsync(
    "حظر مسافر",
    "تم تحديث حالة الحظر في النظام",
    "warning"
);

            await NotifyDashboardUpdate();

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unblock(int id)
        {
            if (!await CanUnblockTravelers())
                return Forbid();

            var traveler = await _context.Travelers.FindAsync(id);

            if (traveler == null)
                return NotFound();

            traveler.IsBlocked = false;
            traveler.BlockReason = null;
            traveler.BlockedAt = null;

            await _context.SaveChangesAsync();
            await _auditService.LogAsync(
    "Unblock",
    "Traveler",
    traveler.Id,
    traveler.PassportNumber,
    traveler.FullName,
    "تم رفع الحظر عن المسافر"
);
            await _notificationService.NotifyBlocksAsync(
    "رفع الحظر",
    "تم رفع الحظر عن أحد المسافرين",
    "success"
);

            await NotifyDashboardUpdate();

            return RedirectToAction(nameof(Index));
        }

        private string BuildTravelerChanges(Traveler oldTraveler, Traveler newTraveler)
        {
            var changes = new List<string>();

            if (oldTraveler.FullName != newTraveler.FullName)
                changes.Add($"الاسم من [{oldTraveler.FullName}] إلى [{newTraveler.FullName}]");

            if (oldTraveler.PassportNumber != newTraveler.PassportNumber)
                changes.Add($"رقم الجواز من [{oldTraveler.PassportNumber}] إلى [{newTraveler.PassportNumber}]");

            if (oldTraveler.Nationality != newTraveler.Nationality)
                changes.Add($"الجنسية من [{oldTraveler.Nationality}] إلى [{newTraveler.Nationality}]");

            if (oldTraveler.Gender != newTraveler.Gender)
                changes.Add($"الجنس من [{oldTraveler.Gender}] إلى [{newTraveler.Gender}]");

            if (oldTraveler.DateOfBirth != newTraveler.DateOfBirth)
                changes.Add($"تاريخ الميلاد من [{oldTraveler.DateOfBirth:yyyy-MM-dd}] إلى [{newTraveler.DateOfBirth:yyyy-MM-dd}]");

            if (oldTraveler.PhoneNumber != newTraveler.PhoneNumber)
                changes.Add($"رقم الهاتف من [{oldTraveler.PhoneNumber}] إلى [{newTraveler.PhoneNumber}]");

            if (oldTraveler.Notes != newTraveler.Notes)
                changes.Add("تم تعديل الملاحظات");

            if (oldTraveler.PassportExpiryDate != newTraveler.PassportExpiryDate)
                changes.Add($"تاريخ انتهاء الجواز من [{oldTraveler.PassportExpiryDate:yyyy-MM-dd}] إلى [{newTraveler.PassportExpiryDate:yyyy-MM-dd}]");

            return changes.Any()
                ? string.Join(" | ", changes)
                : "تم الحفظ بدون تغييرات";

        }

        private async Task NotifyDashboardUpdate()
        {
            var travelersCount = await _context.Travelers
                .CountAsync(t => !t.IsDeleted);

            var tripsCount = await _context.Trips.CountAsync();

            var blockedCount = await _context.Travelers
                .CountAsync(t => t.IsBlocked && !t.IsDeleted);

            var expiringPassportsCount = await _context.Travelers
                .CountAsync(t =>
                    !t.IsDeleted &&
                    t.PassportExpiryDate.HasValue &&
                    t.PassportExpiryDate <= DateTime.Today.AddMonths(6) &&
                    t.PassportExpiryDate >= DateTime.Today);

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