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
    public class TravelerDocumentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly PermissionService _permissionService;
        private readonly AuditService _auditService;
        private readonly IPdfReportService _pdfReportService;
        private readonly IHubContext<DashboardHub> _hubContext;
        private readonly NotificationService _notificationService;

        public TravelerDocumentsController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            PermissionService permissionService,
            AuditService auditService,
            IPdfReportService pdfReportService,
            IHubContext<DashboardHub> hubContext,
            NotificationService notificationService)
        {
            _context = context;
            _environment = environment;
            _permissionService = permissionService;
            _auditService = auditService;
            _pdfReportService = pdfReportService;
            _hubContext = hubContext;
            _notificationService = notificationService;
        }

        private async Task<bool> CanViewDocuments()
        {
            return await _permissionService.HasPermissionAsync(User, "Documents.View");
        }

        private async Task<bool> CanUploadDocuments()
        {
            return await _permissionService.HasPermissionAsync(User, "Documents.Upload");
        }

        private async Task<bool> CanArchiveDocuments()
        {
            return await _permissionService.HasPermissionAsync(User, "Documents.Archive");
        }

        private async Task<bool> CanRestoreDocuments()
        {
            return await _permissionService.HasPermissionAsync(User, "Documents.Restore");
        }

        private async Task<bool> CanExportReports()
        {
            return await _permissionService.HasPermissionAsync(User, "Reports.Export");
        }

        public async Task<IActionResult> ExportToPdf()
        {
            if (!await CanExportReports())
                return Forbid();

            var documents = await _context.TravelerDocuments
                .AsNoTracking()
                .Include(d => d.Traveler)
                .Where(d => !d.IsDeleted)
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateDocumentsReport(documents);

            return File(pdf, "application/pdf", $"Documents_{DateTime.Now:yyyyMMdd}.pdf");
        }

        public async Task<IActionResult> ExportDeletedToPdf()
        {
            if (!await CanExportReports())
                return Forbid();

            var documents = await _context.TravelerDocuments
                .AsNoTracking()
                .Include(d => d.Traveler)
                .Where(d => d.IsDeleted)
                .OrderByDescending(d => d.DeletedAt)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateDeletedDocumentsReport(documents);

            return File(pdf, "application/pdf", $"DeletedDocuments_{DateTime.Now:yyyyMMdd}.pdf");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(
            int travelerId,
            string documentType,
            IFormFile file,
            string? notes)
        {
            if (!await CanUploadDocuments())
                return Forbid();

            var traveler = await _context.Travelers
                .FirstOrDefaultAsync(t => t.Id == travelerId && !t.IsDeleted);

            if (traveler == null)
                return NotFound();

            if (traveler.IsBlocked)
            {
                TempData["Error"] = "لا يمكن رفع مستندات لمسافر محظور.";
                return RedirectToAction("Details", "Travelers", new { id = travelerId });
            }

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "الرجاء اختيار ملف لرفعه.";
                return RedirectToAction("Details", "Travelers", new { id = travelerId });
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                TempData["Error"] = "نوع الملف غير مسموح. المسموح فقط: JPG, JPEG, PNG, PDF.";
                return RedirectToAction("Details", "Travelers", new { id = travelerId });
            }

            long maxFileSize = 5 * 1024 * 1024;

            if (file.Length > maxFileSize)
            {
                TempData["Error"] = "حجم الملف كبير جداً. الحد الأقصى 5MB.";
                return RedirectToAction("Details", "Travelers", new { id = travelerId });
            }

            var allowedDocumentTypes = new[]
            {
                "PersonalPhoto",
                "Visa",
                "PassportCopy",
                "PDF",
                "Other"
            };

            if (!allowedDocumentTypes.Contains(documentType))
            {
                TempData["Error"] = "نوع المستند غير صحيح.";
                return RedirectToAction("Details", "Travelers", new { id = travelerId });
            }

            string uploadsFolder = Path.Combine(
                _environment.WebRootPath,
                "uploads",
                "documents",
                travelerId.ToString());

            Directory.CreateDirectory(uploadsFolder);

            string safeFileName = Guid.NewGuid() + extension;
            string filePath = Path.Combine(uploadsFolder, safeFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            string relativePath = "/uploads/documents/" + travelerId + "/" + safeFileName;

            var document = new TravelerDocument
            {
                TravelerId = travelerId,
                DocumentType = documentType,
                FileName = Path.GetFileName(file.FileName),
                FilePath = relativePath,
                Notes = notes,
                UploadedAt = DateTime.UtcNow,
                IsDeleted = false
            };

            _context.TravelerDocuments.Add(document);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "UploadDocument",
                "TravelerDocument",
                traveler.Id,
                traveler.PassportNumber,
                traveler.FullName,
                $"تم رفع مستند: {GetDocumentTypeArabic(documentType)} - {document.FileName}"
            );

            await _notificationService.NotifyTravelersAsync(
                "رفع مستند",
                "تم رفع مستند جديد لأحد المسافرين",
                "success"
            );

            await NotifyDashboardUpdate();

            TempData["Success"] = "تم رفع المستند بنجاح.";

            return RedirectToAction("Details", "Travelers", new { id = travelerId });
        }

        public async Task<IActionResult> ExportToExcel()
        {
            if (!await CanExportReports())
                return Forbid();

            var documents = await _context.TravelerDocuments
                .AsNoTracking()
                .Include(d => d.Traveler)
                .Where(d => !d.IsDeleted)
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Documents");

            ws.Cell(1, 1).Value = "المسافر";
            ws.Cell(1, 2).Value = "رقم الجواز";
            ws.Cell(1, 3).Value = "نوع المستند";
            ws.Cell(1, 4).Value = "اسم الملف";
            ws.Cell(1, 5).Value = "تاريخ الرفع";

            int row = 2;

            foreach (var d in documents)
            {
                ws.Cell(row, 1).Value = d.Traveler?.FullName;
                ws.Cell(row, 2).Value = d.Traveler?.PassportNumber;
                ws.Cell(row, 3).Value = GetDocumentTypeArabic(d.DocumentType);
                ws.Cell(row, 4).Value = d.FileName;
                ws.Cell(row, 5).Value = d.UploadedAt.ToString("yyyy-MM-dd HH:mm");
                row++;
            }

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"Documents_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!await CanArchiveDocuments())
                return Forbid();

            var document = await _context.TravelerDocuments
                .Include(d => d.Traveler)
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (document == null)
                return NotFound();

            int travelerId = document.TravelerId;

            document.IsDeleted = true;
            document.DeletedAt = DateTime.UtcNow;
            document.DeletedBy = User.Identity?.Name ?? "Unknown";

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "SoftDeleteDocument",
                "TravelerDocument",
                document.TravelerId,
                document.Traveler?.PassportNumber,
                document.Traveler?.FullName,
                $"تم أرشفة مستند: {GetDocumentTypeArabic(document.DocumentType)} - {document.FileName}"
            );

            await _notificationService.NotifyTravelersAsync(
                "أرشفة مستند",
                "تم نقل مستند إلى الأرشيف",
                "warning"
            );

            await NotifyDashboardUpdate();

            TempData["Warning"] = "تم أرشفة المستند بنجاح.";

            return RedirectToAction("Details", "Travelers", new { id = travelerId });
        }

        public async Task<IActionResult> Deleted()
        {
            if (!await CanRestoreDocuments())
                return Forbid();

            var documents = await _context.TravelerDocuments
                .AsNoTracking()
                .Include(d => d.Traveler)
                .Where(d => d.IsDeleted)
                .OrderByDescending(d => d.DeletedAt)
                .ToListAsync();

            return View(documents);
        }

        public async Task<IActionResult> ExportDeletedToExcel()
        {
            if (!await CanExportReports())
                return Forbid();

            var documents = await _context.TravelerDocuments
                .AsNoTracking()
                .Include(d => d.Traveler)
                .Where(d => d.IsDeleted)
                .OrderByDescending(d => d.DeletedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Deleted Documents");

            worksheet.Cell(1, 1).Value = "ID";
            worksheet.Cell(1, 2).Value = "المسافر";
            worksheet.Cell(1, 3).Value = "رقم الجواز";
            worksheet.Cell(1, 4).Value = "نوع المستند";
            worksheet.Cell(1, 5).Value = "اسم الملف";
            worksheet.Cell(1, 6).Value = "تاريخ الرفع";
            worksheet.Cell(1, 7).Value = "تاريخ الأرشفة";
            worksheet.Cell(1, 8).Value = "تمت بواسطة";

            int row = 2;

            foreach (var d in documents)
            {
                worksheet.Cell(row, 1).Value = d.Id;
                worksheet.Cell(row, 2).Value = d.Traveler?.FullName;
                worksheet.Cell(row, 3).Value = d.Traveler?.PassportNumber;
                worksheet.Cell(row, 4).Value = GetDocumentTypeArabic(d.DocumentType);
                worksheet.Cell(row, 5).Value = d.FileName;
                worksheet.Cell(row, 6).Value = d.UploadedAt.ToString("yyyy-MM-dd HH:mm");
                worksheet.Cell(row, 7).Value = d.DeletedAt?.ToString("yyyy-MM-dd HH:mm");
                worksheet.Cell(row, 8).Value = d.DeletedBy;
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"DeletedDocuments_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            );
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            if (!await CanRestoreDocuments())
                return Forbid();

            var document = await _context.TravelerDocuments
                .Include(d => d.Traveler)
                .FirstOrDefaultAsync(d => d.Id == id && d.IsDeleted);

            if (document == null)
                return NotFound();

            document.IsDeleted = false;
            document.DeletedAt = null;
            document.DeletedBy = null;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(
                "RestoreDocument",
                "TravelerDocument",
                document.TravelerId,
                document.Traveler?.PassportNumber,
                document.Traveler?.FullName,
                $"تم استرجاع مستند: {GetDocumentTypeArabic(document.DocumentType)} - {document.FileName}"
            );

            await _notificationService.NotifyTravelersAsync(
                "استرجاع مستند",
                "تم استرجاع مستند من الأرشيف",
                "success"
            );

            await NotifyDashboardUpdate();

            TempData["Success"] = "تم استرجاع المستند بنجاح.";

            return RedirectToAction(nameof(Deleted));
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

        public async Task<IActionResult> Download(int id)
        {
            if (!await CanViewDocuments())
                return Forbid();

            var document = await _context.TravelerDocuments
                .Include(d => d.Traveler)
                .FirstOrDefaultAsync(d => d.Id == id && !d.IsDeleted);

            if (document == null)
                return NotFound();

            if (document.Traveler == null || document.Traveler.IsDeleted)
                return NotFound();

            var filePath = document.FilePath
                .TrimStart('/')
                .Replace("/", Path.DirectorySeparatorChar.ToString());

            var fullPath = Path.Combine(_environment.WebRootPath, filePath);

            if (!System.IO.File.Exists(fullPath))
                return NotFound();

            var contentType = GetContentType(fullPath);

            return PhysicalFile(fullPath, contentType, document.FileName);
        }

        private static string GetContentType(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();

            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                _ => "application/octet-stream"
            };
        }

        private static string GetDocumentTypeArabic(string type)
        {
            return type switch
            {
                "PersonalPhoto" => "صورة شخصية",
                "Visa" => "تأشيرة",
                "PassportCopy" => "نسخة جواز",
                "PDF" => "ملف PDF",
                "Other" => "أخرى",
                _ => type
            };
        }
    }
}