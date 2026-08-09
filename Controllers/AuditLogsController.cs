using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.Services.PdfReports;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class AuditLogsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;
        private readonly IPdfReportService _pdfReportService;

        public AuditLogsController(
            ApplicationDbContext context,
            PermissionService permissionService,
            IPdfReportService pdfReportService)
        {
            _context = context;
            _permissionService = permissionService;
            _pdfReportService = pdfReportService;
        }

        private async Task<bool> CanViewAuditLogs()
        {
            return await _permissionService.HasPermissionAsync(User, "AuditLogs.View");
        }

        private async Task<bool> CanExportReports()
        {
            return await _permissionService.HasPermissionAsync(User, "Reports.Export");
        }

        public async Task<IActionResult> Index(
            string? search,
            string? actionType,
            DateTime? fromDate,
            DateTime? toDate)
        {
            if (!await CanViewAuditLogs())
                return Forbid();

            var logs = _context.AuditLogs
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                logs = logs.Where(x =>
                    (x.TravelerName != null && x.TravelerName.Contains(search)) ||
                    (x.PassportNumber != null && x.PassportNumber.Contains(search)) ||
                    (x.EmployeeName ?? string.Empty).Contains(search) ||
                    (x.Action ?? string.Empty).Contains(search) ||
                    (x.Details ?? string.Empty).Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(actionType))
            {
                logs = logs.Where(x => x.Action == actionType);
            }

            if (fromDate.HasValue)
            {
                logs = logs.Where(x => x.CreatedAt.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                logs = logs.Where(x => x.CreatedAt.Date <= toDate.Value.Date);
            }

            ViewBag.Search = search;
            ViewBag.ActionType = actionType;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

            return View(await logs
                .OrderByDescending(x => x.CreatedAt)
                .Take(1000)
                .ToListAsync());
        }

        public async Task<IActionResult> ExportToPdf(
            string? search,
            string? actionType,
            DateTime? fromDate,
            DateTime? toDate)
        {
            if (!await CanExportReports())
                return Forbid();

            var logs = ApplyFilters(search, actionType, fromDate, toDate);

            var data = await logs
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var pdf = _pdfReportService.GenerateAuditLogsReport(data);

            return File(pdf, "application/pdf", $"AuditLogs_{DateTime.Now:yyyyMMdd}.pdf");
        }

        public async Task<IActionResult> ExportToExcel(
            string? search,
            string? actionType,
            DateTime? fromDate,
            DateTime? toDate)
        {
            if (!await CanExportReports())
                return Forbid();

            var logs = ApplyFilters(search, actionType, fromDate, toDate);

            var data = await logs
                .OrderByDescending(x => x.CreatedAt)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Audit Logs");

            worksheet.Cell(1, 1).Value = "التاريخ";
            worksheet.Cell(1, 2).Value = "الموظف";
            worksheet.Cell(1, 3).Value = "العملية";
            worksheet.Cell(1, 4).Value = "المسافر";
            worksheet.Cell(1, 5).Value = "رقم الجواز";
            worksheet.Cell(1, 6).Value = "التفاصيل";
            worksheet.Cell(1, 7).Value = "IP";
            worksheet.Cell(1, 8).Value = "الجهاز";

            int row = 2;

            foreach (var item in data)
            {
                worksheet.Cell(row, 1).Value = item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
                worksheet.Cell(row, 2).Value = item.EmployeeName;
                worksheet.Cell(row, 3).Value = item.Action;
                worksheet.Cell(row, 4).Value = item.TravelerName;
                worksheet.Cell(row, 5).Value = item.PassportNumber;
                worksheet.Cell(row, 6).Value = item.Details;
                worksheet.Cell(row, 7).Value = item.IpAddress;
                worksheet.Cell(row, 8).Value = item.UserAgent;

                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"AuditLogs_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            );
        }

        private IQueryable<Models.AuditLog> ApplyFilters(
            string? search,
            string? actionType,
            DateTime? fromDate,
            DateTime? toDate)
        {
            var logs = _context.AuditLogs
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                logs = logs.Where(x =>
                    (x.TravelerName != null && x.TravelerName.Contains(search)) ||
                    (x.PassportNumber != null && x.PassportNumber.Contains(search)) ||
                    (x.EmployeeName ?? string.Empty).Contains(search) ||
                    (x.Action ?? string.Empty).Contains(search) ||
                    (x.Details ?? string.Empty).Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(actionType))
            {
                logs = logs.Where(x => x.Action == actionType);
            }

            if (fromDate.HasValue)
            {
                logs = logs.Where(x => x.CreatedAt.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                logs = logs.Where(x => x.CreatedAt.Date <= toDate.Value.Date);
            }

            return logs;
        }
    }
}
