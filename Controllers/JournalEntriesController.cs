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
    public class JournalEntriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public JournalEntriesController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.JournalEntries");
        }

        public async Task<IActionResult> Index(
            string? search,
            DateTime? fromDate,
            DateTime? toDate,
            bool? isPosted)
        {
            if (!await CanAccess())
                return Forbid();

            var entries = _context.JournalEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                entries = entries.Where(x =>
                    x.EntryNumber.Contains(search) ||
                    x.Description.Contains(search) ||
                    x.SourceType.Contains(search));
            }

            if (fromDate.HasValue)
            {
                entries = entries.Where(x => x.EntryDate.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                entries = entries.Where(x => x.EntryDate.Date <= toDate.Value.Date);
            }

            if (isPosted.HasValue)
            {
                entries = entries.Where(x => x.IsPosted == isPosted.Value);
            }

            ViewBag.Search = search;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.IsPosted = isPosted;

            return View(await entries
                .OrderByDescending(x => x.EntryDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync());
        }

        public async Task<IActionResult> Details(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .ThenInclude(x => x.Account)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entry == null)
                return NotFound();

            ViewBag.TotalDebit = entry.Lines.Sum(x => x.Debit);
            ViewBag.TotalCredit = entry.Lines.Sum(x => x.Credit);

            return View(entry);
        }

        public async Task<IActionResult> Create()
        {
            if (!await CanAccess())
                return Forbid();

            await LoadAccounts();

            var entry = new JournalEntry
            {
                EntryDate = DateTime.Now,
                EntryNumber = await GenerateEntryNumber(),
                SourceType = "Manual",
                IsPosted = true,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine(),
                    new JournalEntryLine()
                }
            };

            return View(entry);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(JournalEntry entry)
        {
            if (!await CanAccess())
                return Forbid();

            entry.Lines = entry.Lines
                .Where(x => x.AccountId > 0 && (x.Debit > 0 || x.Credit > 0))
                .ToList();

            if (!entry.Lines.Any())
            {
                ModelState.AddModelError("", "يجب إدخال طرفين على الأقل للقيد.");
            }

            if (entry.Lines.Count < 2)
            {
                ModelState.AddModelError("", "القيد المحاسبي يجب أن يحتوي على سطرين على الأقل.");
            }

            foreach (var line in entry.Lines)
            {
                if (line.Debit > 0 && line.Credit > 0)
                {
                    ModelState.AddModelError("", "لا يمكن أن يكون السطر مدين ودائن في نفس الوقت.");
                }

                if (line.Debit <= 0 && line.Credit <= 0)
                {
                    ModelState.AddModelError("", "كل سطر يجب أن يحتوي على مبلغ مدين أو دائن.");
                }
            }

            var totalDebit = entry.Lines.Sum(x => x.Debit);
            var totalCredit = entry.Lines.Sum(x => x.Credit);

            if (totalDebit != totalCredit)
            {
                ModelState.AddModelError("", "القيد غير متوازن. إجمالي المدين يجب أن يساوي إجمالي الدائن.");
            }

            var accountIds = entry.Lines.Select(x => x.AccountId).Distinct().ToList();

            var validAccountsCount = await _context.Accounts
                .AsNoTracking()
                .CountAsync(x => accountIds.Contains(x.Id) && x.IsActive);

            if (validAccountsCount != accountIds.Count)
            {
                ModelState.AddModelError("", "يوجد حساب غير صحيح أو غير فعال داخل القيد.");
            }

            if (await _context.JournalEntries.AnyAsync(x => x.EntryNumber == entry.EntryNumber))
            {
                entry.EntryNumber = await GenerateEntryNumber();
            }

            if (!ModelState.IsValid)
            {
                await LoadAccounts();
                return View(entry);
            }

            entry.CreatedAt = DateTime.Now;
            entry.SourceType = string.IsNullOrWhiteSpace(entry.SourceType)
                ? "Manual"
                : entry.SourceType;

            _context.JournalEntries.Add(entry);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إنشاء القيد اليومي بنجاح.";

            return RedirectToAction(nameof(Details), new { id = entry.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePosted(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var entry = await _context.JournalEntries
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entry == null)
                return NotFound();

            if (!entry.Lines.Any() || entry.Lines.Sum(x => x.Debit) != entry.Lines.Sum(x => x.Credit))
            {
                TempData["Error"] = "لا يمكن ترحيل قيد غير متوازن.";
                return RedirectToAction(nameof(Details), new { id });
            }

            entry.IsPosted = !entry.IsPosted;

            await _context.SaveChangesAsync();

            TempData["Success"] = entry.IsPosted
                ? "تم ترحيل القيد بنجاح."
                : "تم إلغاء ترحيل القيد بنجاح.";

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task LoadAccounts()
        {
            var accounts = await _context.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new
                {
                    x.Id,
                    Name = x.Code + " - " + x.Name
                })
                .ToListAsync();

            ViewBag.Accounts = new SelectList(accounts, "Id", "Name");
        }

        private async Task<string> GenerateEntryNumber()
        {
            var year = DateTime.Now.Year;
            var prefix = $"JE-{year}-";

            var count = await _context.JournalEntries
                .AsNoTracking()
                .CountAsync(x => x.EntryNumber.StartsWith(prefix));

            return $"{prefix}{count + 1:00000}";
        }
    }
}