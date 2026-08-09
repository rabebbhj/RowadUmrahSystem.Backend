using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models.Accounting;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class AccountsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public AccountsController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.ChartOfAccounts");
        }

        public async Task<IActionResult> Index(string? search, AccountType? type, bool? isActive)
        {
            if (!await CanAccess())
                return Forbid();

            var accounts = _context.Accounts
                .AsNoTracking()
                .Include(x => x.ParentAccount)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                accounts = accounts.Where(x =>
                    x.Code.Contains(search) ||
                    x.Name.Contains(search));
            }

            if (type.HasValue)
            {
                accounts = accounts.Where(x => x.Type == type.Value);
            }

            if (isActive.HasValue)
            {
                accounts = accounts.Where(x => x.IsActive == isActive.Value);
            }

            ViewBag.Search = search;
            ViewBag.Type = type;
            ViewBag.IsActive = isActive;

            return View(await accounts
                .OrderBy(x => x.Code)
                .ToListAsync());
        }

        public async Task<IActionResult> Details(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var account = await _context.Accounts
                .AsNoTracking()
                .Include(x => x.ParentAccount)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (account == null)
                return NotFound();

            var lines = await _context.JournalEntryLines
                .AsNoTracking()
                .Include(x => x.JournalEntry)
                .Where(x => x.AccountId == id)
                .OrderByDescending(x => x.JournalEntry.EntryDate)
                .Take(100)
                .ToListAsync();

            ViewBag.TotalDebit = lines.Sum(x => x.Debit);
            ViewBag.TotalCredit = lines.Sum(x => x.Credit);
            ViewBag.Balance = lines.Sum(x => x.Debit) - lines.Sum(x => x.Credit);
            ViewBag.Lines = lines;

            return View(account);
        }

        public async Task<IActionResult> Create()
        {
            if (!await CanAccess())
                return Forbid();

            await LoadLookups();

            return View(new Account());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Account account)
        {
            if (!await CanAccess())
                return Forbid();

            if (await _context.Accounts.AnyAsync(x => x.Code == account.Code))
            {
                ModelState.AddModelError("Code", "رقم الحساب مستخدم مسبقاً.");
            }

            if (account.ParentAccountId.HasValue)
            {
                var parent = await _context.Accounts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == account.ParentAccountId.Value);

                if (parent == null)
                {
                    ModelState.AddModelError("ParentAccountId", "الحساب الرئيسي غير موجود.");
                }
                else if (parent.Type != account.Type)
                {
                    ModelState.AddModelError("ParentAccountId", "نوع الحساب الفرعي يجب أن يطابق نوع الحساب الرئيسي.");
                }
            }

            if (!ModelState.IsValid)
            {
                await LoadLookups(account.ParentAccountId);
                return View(account);
            }

            account.CreatedAt = DateTime.Now;
            account.IsActive = true;

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إنشاء الحساب بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var account = await _context.Accounts.FindAsync(id);

            if (account == null)
                return NotFound();

            await LoadLookups(account.ParentAccountId, account.Id);

            return View(account);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Account account)
        {
            if (!await CanAccess())
                return Forbid();

            if (id != account.Id)
                return NotFound();

            var existing = await _context.Accounts.FirstOrDefaultAsync(x => x.Id == id);

            if (existing == null)
                return NotFound();

            if (existing.IsSystemAccount && existing.Code != account.Code)
            {
                ModelState.AddModelError("Code", "لا يمكن تعديل رقم حساب نظامي.");
            }

            if (await _context.Accounts.AnyAsync(x => x.Code == account.Code && x.Id != id))
            {
                ModelState.AddModelError("Code", "رقم الحساب مستخدم مسبقاً.");
            }

            if (account.ParentAccountId == account.Id)
            {
                ModelState.AddModelError("ParentAccountId", "لا يمكن جعل الحساب تابعاً لنفسه.");
            }

            if (account.ParentAccountId.HasValue)
            {
                var parent = await _context.Accounts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == account.ParentAccountId.Value);

                if (parent == null)
                {
                    ModelState.AddModelError("ParentAccountId", "الحساب الرئيسي غير موجود.");
                }
                else if (parent.Type != account.Type)
                {
                    ModelState.AddModelError("ParentAccountId", "نوع الحساب الفرعي يجب أن يطابق نوع الحساب الرئيسي.");
                }
            }

            if (!ModelState.IsValid)
            {
                await LoadLookups(account.ParentAccountId, account.Id);
                return View(account);
            }

            existing.Code = account.Code;
            existing.Name = account.Name;
            existing.Type = account.Type;
            existing.ParentAccountId = account.ParentAccountId;
            existing.IsActive = account.IsActive;

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تعديل الحساب بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var account = await _context.Accounts.FindAsync(id);

            if (account == null)
                return NotFound();

            if (account.IsSystemAccount)
            {
                TempData["Error"] = "لا يمكن تعطيل حساب نظامي.";
                return RedirectToAction(nameof(Index));
            }

            account.IsActive = !account.IsActive;

            await _context.SaveChangesAsync();

            TempData["Success"] = account.IsActive
                ? "تم تفعيل الحساب بنجاح."
                : "تم تعطيل الحساب بنجاح.";

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> ExportToExcel()
        {
            if (!await CanAccess())
                return Forbid();

            var accounts = await _context.Accounts
                .AsNoTracking()
                .Include(x => x.ParentAccount)
                .OrderBy(x => x.Code)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Chart Of Accounts");

            worksheet.Cell(1, 1).Value = "رقم الحساب";
            worksheet.Cell(1, 2).Value = "اسم الحساب";
            worksheet.Cell(1, 3).Value = "النوع";
            worksheet.Cell(1, 4).Value = "الحساب الرئيسي";
            worksheet.Cell(1, 5).Value = "الحالة";
            worksheet.Cell(1, 6).Value = "نظامي";

            int row = 2;

            foreach (var item in accounts)
            {
                worksheet.Cell(row, 1).Value = item.Code;
                worksheet.Cell(row, 2).Value = item.Name;
                worksheet.Cell(row, 3).Value = GetAccountTypeName(item.Type);
                worksheet.Cell(row, 4).Value = item.ParentAccount?.Name ?? "-";
                worksheet.Cell(row, 5).Value = item.IsActive ? "فعال" : "معطل";
                worksheet.Cell(row, 6).Value = item.IsSystemAccount ? "نعم" : "لا";

                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ChartOfAccounts_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
            );
        }

        private async Task LoadLookups(int? selectedParentId = null, int? currentAccountId = null)
        {
            var parentAccounts = await _context.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive && x.Id != currentAccountId)
                .OrderBy(x => x.Code)
                .ToListAsync();

            ViewBag.ParentAccounts = new SelectList(
                parentAccounts.Select(x => new
                {
                    x.Id,
                    Name = $"{x.Code} - {x.Name}"
                }),
                "Id",
                "Name",
                selectedParentId
            );

            ViewBag.AccountTypes = new SelectList(Enum.GetValues<AccountType>()
                .Select(x => new
                {
                    Id = x,
                    Name = GetAccountTypeName(x)
                }),
                "Id",
                "Name"
            );
        }

        private string GetAccountTypeName(AccountType type)
        {
            return type switch
            {
                AccountType.Asset => "أصول",
                AccountType.Liability => "التزامات",
                AccountType.Equity => "حقوق ملكية",
                AccountType.Revenue => "إيرادات",
                AccountType.Expense => "مصروفات",
                _ => "غير محدد"
            };
        }
    }
}