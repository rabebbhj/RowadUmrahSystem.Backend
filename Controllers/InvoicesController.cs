using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models.Accounting;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.ViewModels.Accounting;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class InvoicesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public InvoicesController(ApplicationDbContext context, PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.Invoices");
        }

        public async Task<IActionResult> Index(string? search, InvoiceStatus? status)
        {
            if (!await CanAccess())
                return Forbid();

            var invoices = _context.Invoices
                .AsNoTracking()
                .Include(x => x.Traveler)
                .Include(x => x.Trip)
                .Include(x => x.Currency)
                .Where(x => !x.IsArchived)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                invoices = invoices.Where(x =>
                    x.InvoiceNumber.Contains(search) ||
                    x.CustomerName.Contains(search) ||
                    (x.PassportNumber != null && x.PassportNumber.Contains(search)));
            }

            if (status.HasValue)
            {
                invoices = invoices.Where(x => x.Status == status.Value);
            }

            ViewBag.Search = search;
            ViewBag.Status = status;

            return View(await invoices
                .OrderByDescending(x => x.InvoiceDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync());
        }

        public async Task<IActionResult> Details(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(x => x.Items.OrderBy(i => i.SortOrder))
                .Include(x => x.Traveler)
                .Include(x => x.Trip)
                .Include(x => x.Currency)
                .Include(x => x.CostCenter)
                .Include(x => x.PaymentTerm)
                .Include(x => x.JournalEntry)
                    .ThenInclude(x => x!.Lines)
                    .ThenInclude(x => x.Account)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (invoice == null)
                return NotFound();

            return View(invoice);
        }

        public async Task<IActionResult> Create()
        {
            if (!await CanAccess())
                return Forbid();

            await LoadLookups();

            return View(new InvoiceFormViewModel
            {
                InvoiceDate = DateTime.Now,
                DueDate = DateTime.Now.AddDays(7)
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(InvoiceFormViewModel model)
        {
            if (!await CanAccess())
                return Forbid();

            model.Items = model.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Description) && x.Quantity > 0 && x.UnitPrice > 0)
                .ToList();

            if (!model.Items.Any())
                ModelState.AddModelError("", "يجب إضافة خدمة واحدة على الأقل داخل الفاتورة.");

            decimal subTotal = 0;
            decimal discountTotal = 0;
            decimal taxTotal = 0;
            decimal grandTotal = 0;

            foreach (var item in model.Items)
            {
                var lineSubTotal = item.Quantity * item.UnitPrice;
                var discount = item.DiscountAmount;
                var taxableAmount = lineSubTotal - discount;

                if (taxableAmount < 0)
                    taxableAmount = 0;

                var tax = taxableAmount * (item.TaxRate / 100);
                var lineTotal = taxableAmount + tax;

                subTotal += lineSubTotal;
                discountTotal += discount;
                taxTotal += tax;
                grandTotal += lineTotal;
            }

            if (grandTotal <= 0)
                ModelState.AddModelError("", "إجمالي الفاتورة يجب أن يكون أكبر من صفر.");

            var receivableAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == model.ReceivableAccountId && x.IsActive);

            var revenueAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == model.RevenueAccountId && x.IsActive);

            if (receivableAccount == null)
                ModelState.AddModelError("ReceivableAccountId", "حساب العملاء غير صحيح.");

            if (revenueAccount == null)
                ModelState.AddModelError("RevenueAccountId", "حساب الإيرادات غير صحيح.");

            if (!ModelState.IsValid)
            {
                await LoadLookups();
                return View(model);
            }

            var invoiceNumber = await GenerateInvoiceNumber();
            var entryNumber = await GenerateEntryNumber();

            var journalEntry = new JournalEntry
            {
                EntryNumber = entryNumber,
                EntryDate = model.InvoiceDate,
                Description = $"فاتورة رقم {invoiceNumber} - {model.CustomerName}",
                SourceType = "Invoice",
                IsPosted = true,
                CreatedAt = DateTime.Now,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        AccountId = model.ReceivableAccountId,
                        Debit = grandTotal,
                        Credit = 0,
                        Notes = "إثبات ذمة العميل"
                    },
                    new JournalEntryLine
                    {
                        AccountId = model.RevenueAccountId,
                        Debit = 0,
                        Credit = grandTotal,
                        Notes = "إثبات إيراد الفاتورة"
                    }
                }
            };

            var sort = 1;

            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNumber,
                InvoiceDate = model.InvoiceDate,
                DueDate = model.DueDate,
                CustomerName = model.CustomerName,
                PassportNumber = model.PassportNumber,
                TravelerId = model.TravelerId,
                TripId = model.TripId,
                CurrencyId = model.CurrencyId,
                CostCenterId = model.CostCenterId,
                PaymentTermId = model.PaymentTermId,
                PaymentMethod = model.PaymentMethod,
                ReferenceNumber = model.ReferenceNumber,
                SubTotal = subTotal,
                DiscountAmount = discountTotal,
                TaxAmount = taxTotal,
                TotalAmount = grandTotal,
                PaidAmount = 0,
                Status = InvoiceStatus.Unpaid,
                Notes = model.Notes,
                CreatedAt = DateTime.Now,
                JournalEntry = journalEntry,
                Items = model.Items.Select(x =>
                {
                    var lineSubTotal = x.Quantity * x.UnitPrice;
                    var taxableAmount = lineSubTotal - x.DiscountAmount;

                    if (taxableAmount < 0)
                        taxableAmount = 0;

                    var tax = taxableAmount * (x.TaxRate / 100);
                    var lineTotal = taxableAmount + tax;

                    return new InvoiceItem
                    {
                        Description = x.Description,
                        Quantity = x.Quantity,
                        UnitPrice = x.UnitPrice,
                        DiscountAmount = x.DiscountAmount,
                        TaxRate = x.TaxRate,
                        LineTotal = lineTotal,
                        SortOrder = sort++
                    };
                }).ToList()
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إنشاء الفاتورة والقيد المحاسبي تلقائيًا.";

            return RedirectToAction(nameof(Details), new { id = invoice.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var invoice = await _context.Invoices.FirstOrDefaultAsync(x => x.Id == id);

            if (invoice == null)
                return NotFound();

            if (invoice.Status == InvoiceStatus.Paid)
            {
                TempData["Error"] = "لا يمكن إلغاء فاتورة مدفوعة.";
                return RedirectToAction(nameof(Details), new { id });
            }

            invoice.Status = InvoiceStatus.Cancelled;

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إلغاء الفاتورة بنجاح.";

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task LoadLookups()
        {
            ViewBag.Travelers = new SelectList(
                await _context.Travelers
                    .AsNoTracking()
                    .OrderBy(x => x.FullName)
                    .Select(x => new
                    {
                        x.Id,
                        Name = x.FullName + " - " + x.PassportNumber
                    })
                    .ToListAsync(),
                "Id",
                "Name"
            );

            ViewBag.Trips = new SelectList(
                await _context.Trips
                    .AsNoTracking()
                    .Include(x => x.Traveler)
                    .OrderByDescending(x => x.TripDate)
                    .Select(x => new
                    {
                        x.Id,
                        Name = x.Traveler.FullName + " - " + x.TripType + " - " + x.TripDate.ToString("yyyy-MM-dd")
                    })
                    .ToListAsync(),
                "Id",
                "Name"
            );

            ViewBag.Currencies = new SelectList(
                await _context.Currencies
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Code)
                    .Select(x => new
                    {
                        x.Id,
                        Name = x.Code + " - " + x.Name
                    })
                    .ToListAsync(),
                "Id",
                "Name"
            );

            ViewBag.CostCenters = new SelectList(
                await _context.CostCenters
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.Code)
                    .Select(x => new
                    {
                        x.Id,
                        Name = x.Code + " - " + x.Name
                    })
                    .ToListAsync(),
                "Id",
                "Name"
            );

            ViewBag.PaymentTerms = new SelectList(
                await _context.PaymentTerms
                    .AsNoTracking()
                    .Where(x => x.IsActive)
                    .OrderBy(x => x.DueDays)
                    .Select(x => new
                    {
                        x.Id,
                        Name = x.Name
                    })
                    .ToListAsync(),
                "Id",
                "Name"
            );

            ViewBag.ReceivableAccounts = new SelectList(
                await _context.Accounts
                    .AsNoTracking()
                    .Where(x => x.IsActive && x.Type == AccountType.Asset)
                    .OrderBy(x => x.Code)
                    .Select(x => new
                    {
                        x.Id,
                        Name = x.Code + " - " + x.Name
                    })
                    .ToListAsync(),
                "Id",
                "Name"
            );

            ViewBag.RevenueAccounts = new SelectList(
                await _context.Accounts
                    .AsNoTracking()
                    .Where(x => x.IsActive && x.Type == AccountType.Revenue)
                    .OrderBy(x => x.Code)
                    .Select(x => new
                    {
                        x.Id,
                        Name = x.Code + " - " + x.Name
                    })
                    .ToListAsync(),
                "Id",
                "Name"
            );
        }

        private async Task<string> GenerateInvoiceNumber()
        {
            var year = DateTime.Now.Year;
            var prefix = $"INV-{year}-";

            var count = await _context.Invoices
                .AsNoTracking()
                .CountAsync(x => x.InvoiceNumber.StartsWith(prefix));

            return $"{prefix}{count + 1:00000}";
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