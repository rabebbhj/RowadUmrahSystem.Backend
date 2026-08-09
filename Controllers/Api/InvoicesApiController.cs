using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models.Accounting;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/invoices")]
    public class InvoicesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public InvoicesApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<InvoiceListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] InvoiceStatus? status = null)
        {
            if (!await CanAccess())
                return Forbid();

            var query = _context.Invoices
                .AsNoTracking()
                .Where(x => !x.IsArchived)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.InvoiceNumber.Contains(search) ||
                    x.CustomerName.Contains(search) ||
                    (x.PassportNumber != null && x.PassportNumber.Contains(search)));
            }

            if (status.HasValue)
            {
                query = query.Where(x => x.Status == status.Value);
            }

            var invoices = await query
                .OrderByDescending(x => x.InvoiceDate)
                .ThenByDescending(x => x.Id)
                .Select(x => new InvoiceListItemDto(
                    x.Id,
                    x.InvoiceNumber,
                    x.InvoiceDate,
                    x.DueDate,
                    x.CustomerName,
                    x.PassportNumber,
                    x.TravelerId,
                    x.Traveler != null ? x.Traveler.FullName : null,
                    x.TripId,
                    x.Trip != null
                        ? x.Trip.Traveler.FullName + " - " + x.Trip.TripType + " - " + x.Trip.TripDate.ToString("yyyy-MM-dd")
                        : null,
                    x.CurrencyId,
                    x.Currency != null ? x.Currency.Code : null,
                    x.TotalAmount,
                    x.PaidAmount,
                    x.TotalAmount - x.PaidAmount,
                    x.Status,
                    x.CreatedAt))
                .ToListAsync();

            return Ok(invoices);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<InvoiceDetailDto>> GetById(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(x => x.Items.OrderBy(item => item.SortOrder))
                .Include(x => x.Traveler)
                .Include(x => x.Trip)
                    .ThenInclude(x => x!.Traveler)
                .Include(x => x.Currency)
                .Include(x => x.CostCenter)
                .Include(x => x.PaymentTerm)
                .Include(x => x.JournalEntry)
                    .ThenInclude(x => x!.Lines)
                    .ThenInclude(x => x.Account)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (invoice == null)
                return NotFound();

            return Ok(MapDetail(invoice));
        }

        [HttpGet("lookups")]
        public async Task<ActionResult<InvoiceLookupsDto>> GetLookups()
        {
            if (!await CanAccess())
                return Forbid();

            var travelers = await _context.Travelers
                .AsNoTracking()
                .OrderBy(x => x.FullName)
                .Select(x => new LookupOptionDto(
                    x.Id,
                    x.FullName + " - " + x.PassportNumber))
                .ToListAsync();

            var trips = await _context.Trips
                .AsNoTracking()
                .Include(x => x.Traveler)
                .OrderByDescending(x => x.TripDate)
                .Select(x => new LookupOptionDto(
                    x.Id,
                    x.Traveler.FullName + " - " + x.TripType + " - " + x.TripDate.ToString("yyyy-MM-dd")))
                .ToListAsync();

            var currencies = await _context.Currencies
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new LookupOptionDto(x.Id, x.Code + " - " + x.Name))
                .ToListAsync();

            var costCenters = await _context.CostCenters
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new LookupOptionDto(x.Id, x.Code + " - " + x.Name))
                .ToListAsync();

            var paymentTerms = await _context.PaymentTerms
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.DueDays)
                .Select(x => new LookupOptionDto(x.Id, x.Name))
                .ToListAsync();

            var receivableAccounts = await _context.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive && x.Type == AccountType.Asset)
                .OrderBy(x => x.Code)
                .Select(x => new LookupOptionDto(x.Id, x.Code + " - " + x.Name))
                .ToListAsync();

            var revenueAccounts = await _context.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive && x.Type == AccountType.Revenue)
                .OrderBy(x => x.Code)
                .Select(x => new LookupOptionDto(x.Id, x.Code + " - " + x.Name))
                .ToListAsync();

            return Ok(new InvoiceLookupsDto(
                travelers,
                trips,
                currencies,
                costCenters,
                paymentTerms,
                receivableAccounts,
                revenueAccounts));
        }

        [HttpPost]
        public async Task<ActionResult<InvoiceDetailDto>> Create([FromBody] InvoiceUpsertRequestDto request)
        {
            if (!await CanAccess())
                return Forbid();

            var validationError = await ValidateAsync(request);
            if (validationError != null)
                return BadRequest(validationError);

            var items = NormalizeItems(request.Items);
            var invoiceNumber = await GenerateInvoiceNumber();
            var entryNumber = await GenerateEntryNumber();

            var totals = CalculateTotals(items);

            var journalEntry = new JournalEntry
            {
                EntryNumber = entryNumber,
                EntryDate = request.InvoiceDate,
                Description = $"فاتورة رقم {invoiceNumber} - {request.CustomerName.Trim()}",
                SourceType = "Invoice",
                IsPosted = true,
                CreatedAt = DateTime.Now,
                Lines = new List<JournalEntryLine>
                {
                    new JournalEntryLine
                    {
                        AccountId = request.ReceivableAccountId,
                        Debit = totals.GrandTotal,
                        Credit = 0,
                        Notes = "إثبات ذمة العميل"
                    },
                    new JournalEntryLine
                    {
                        AccountId = request.RevenueAccountId,
                        Debit = 0,
                        Credit = totals.GrandTotal,
                        Notes = "إثبات إيراد الفاتورة"
                    }
                }
            };

            var sortOrder = 1;
            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNumber,
                InvoiceDate = request.InvoiceDate,
                DueDate = request.DueDate,
                CustomerName = request.CustomerName.Trim(),
                PassportNumber = request.PassportNumber?.Trim(),
                TravelerId = request.TravelerId,
                TripId = request.TripId,
                CurrencyId = request.CurrencyId,
                CostCenterId = request.CostCenterId,
                PaymentTermId = request.PaymentTermId,
                PaymentMethod = request.PaymentMethod,
                ReferenceNumber = request.ReferenceNumber?.Trim() ?? string.Empty,
                SubTotal = totals.SubTotal,
                DiscountAmount = totals.DiscountAmount,
                TaxAmount = totals.TaxAmount,
                TotalAmount = totals.GrandTotal,
                PaidAmount = 0,
                Status = InvoiceStatus.Unpaid,
                Notes = request.Notes?.Trim() ?? string.Empty,
                CreatedAt = DateTime.Now,
                JournalEntry = journalEntry,
                Items = items.Select(item => new InvoiceItem
                {
                    Description = item.Description.Trim(),
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    TaxRate = item.TaxRate,
                    LineTotal = CalculateLineTotal(item),
                    SortOrder = sortOrder++
                }).ToList()
            };

            _context.Invoices.Add(invoice);
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(invoice.Id));
        }

        [HttpPost("{id:int}/cancel")]
        public async Task<ActionResult<InvoiceDetailDto>> Cancel(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var invoice = await _context.Invoices.FirstOrDefaultAsync(x => x.Id == id);
            if (invoice == null)
                return NotFound();

            if (invoice.Status == InvoiceStatus.Paid)
                return BadRequest("لا يمكن إلغاء فاتورة مدفوعة.");

            invoice.Status = InvoiceStatus.Cancelled;
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(invoice.Id));
        }

        private async Task<string?> ValidateAsync(InvoiceUpsertRequestDto request)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.CustomerName))
                errors.Add("اسم العميل مطلوب.");

            var items = NormalizeItems(request.Items);
            if (items.Count == 0)
                errors.Add("يجب إضافة بند واحد على الأقل داخل الفاتورة.");

            if (!Enum.IsDefined(typeof(PaymentMethod), request.PaymentMethod))
                errors.Add("طريقة الدفع غير صحيحة.");

            var totals = CalculateTotals(items);
            if (totals.GrandTotal <= 0)
                errors.Add("إجمالي الفاتورة يجب أن يكون أكبر من صفر.");

            if (request.TravelerId.HasValue)
            {
                var travelerExists = await _context.Travelers.AnyAsync(x => x.Id == request.TravelerId.Value);
                if (!travelerExists)
                    errors.Add("المسافر المحدد غير موجود.");
            }

            if (request.TripId.HasValue)
            {
                var tripExists = await _context.Trips.AnyAsync(x => x.Id == request.TripId.Value);
                if (!tripExists)
                    errors.Add("الرحلة المحددة غير موجودة.");
            }

            if (request.CurrencyId.HasValue)
            {
                var currencyExists = await _context.Currencies.AnyAsync(x => x.Id == request.CurrencyId.Value && x.IsActive);
                if (!currencyExists)
                    errors.Add("العملة المحددة غير متاحة.");
            }

            if (request.CostCenterId.HasValue)
            {
                var costCenterExists = await _context.CostCenters.AnyAsync(x => x.Id == request.CostCenterId.Value && x.IsActive);
                if (!costCenterExists)
                    errors.Add("مركز التكلفة المحدد غير متاح.");
            }

            if (request.PaymentTermId.HasValue)
            {
                var paymentTermExists = await _context.PaymentTerms.AnyAsync(x => x.Id == request.PaymentTermId.Value && x.IsActive);
                if (!paymentTermExists)
                    errors.Add("شرط الدفع المحدد غير متاح.");
            }

            var receivableAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.ReceivableAccountId && x.IsActive);

            if (receivableAccount == null || receivableAccount.Type != AccountType.Asset)
                errors.Add("حساب الذمم المدينة غير صحيح.");

            var revenueAccount = await _context.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.RevenueAccountId && x.IsActive);

            if (revenueAccount == null || revenueAccount.Type != AccountType.Revenue)
                errors.Add("حساب الإيرادات غير صحيح.");

            return errors.Count > 0 ? string.Join(" ", errors) : null;
        }

        private static List<InvoiceItemUpsertRequestDto> NormalizeItems(IEnumerable<InvoiceItemUpsertRequestDto>? items)
        {
            return (items ?? Enumerable.Empty<InvoiceItemUpsertRequestDto>())
                .Where(item =>
                    !string.IsNullOrWhiteSpace(item.Description) &&
                    item.Quantity > 0 &&
                    item.UnitPrice > 0)
                .Select(item => new InvoiceItemUpsertRequestDto
                {
                    Description = item.Description.Trim(),
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = Math.Max(0, item.DiscountAmount),
                    TaxRate = Math.Max(0, item.TaxRate)
                })
                .ToList();
        }

        private static (decimal SubTotal, decimal DiscountAmount, decimal TaxAmount, decimal GrandTotal) CalculateTotals(
            IReadOnlyCollection<InvoiceItemUpsertRequestDto> items)
        {
            decimal subTotal = 0;
            decimal discountTotal = 0;
            decimal taxTotal = 0;
            decimal grandTotal = 0;

            foreach (var item in items)
            {
                var lineTotal = CalculateLineTotal(item);
                var lineSubTotal = item.Quantity * item.UnitPrice;
                var discount = Math.Max(0, item.DiscountAmount);
                var taxableAmount = lineSubTotal - discount;

                if (taxableAmount < 0)
                    taxableAmount = 0;

                var tax = taxableAmount * (Math.Max(0, item.TaxRate) / 100);

                subTotal += lineSubTotal;
                discountTotal += discount;
                taxTotal += tax;
                grandTotal += lineTotal;
            }

            return (subTotal, discountTotal, taxTotal, grandTotal);
        }

        private static decimal CalculateLineTotal(InvoiceItemUpsertRequestDto item)
        {
            var lineSubTotal = item.Quantity * item.UnitPrice;
            var discount = Math.Max(0, item.DiscountAmount);
            var taxableAmount = lineSubTotal - discount;

            if (taxableAmount < 0)
                taxableAmount = 0;

            var tax = taxableAmount * (Math.Max(0, item.TaxRate) / 100);
            return taxableAmount + tax;
        }

        private InvoiceDetailDto MapDetail(Invoice invoice)
        {
            var tripLabel = invoice.Trip != null
                ? invoice.Trip.Traveler.FullName + " - " + invoice.Trip.TripType + " - " + invoice.Trip.TripDate.ToString("yyyy-MM-dd")
                : null;

            return new InvoiceDetailDto(
                invoice.Id,
                invoice.InvoiceNumber,
                invoice.InvoiceDate,
                invoice.DueDate,
                invoice.CustomerName,
                invoice.PassportNumber,
                invoice.TravelerId,
                invoice.Traveler?.FullName,
                invoice.TripId,
                tripLabel,
                invoice.CurrencyId,
                invoice.Currency?.Code,
                invoice.CostCenterId,
                invoice.CostCenter != null ? invoice.CostCenter.Code + " - " + invoice.CostCenter.Name : null,
                invoice.PaymentTermId,
                invoice.PaymentTerm?.Name,
                invoice.PaymentMethod,
                invoice.ReferenceNumber,
                invoice.Notes,
                invoice.SubTotal,
                invoice.DiscountAmount,
                invoice.TaxAmount,
                invoice.TotalAmount,
                invoice.PaidAmount,
                invoice.RemainingAmount,
                invoice.Status,
                invoice.IsArchived,
                invoice.PaidAt,
                invoice.JournalEntryId,
                invoice.JournalEntry?.EntryNumber,
                invoice.JournalEntry?.EntryDate,
                invoice.JournalEntry?.Description,
                invoice.JournalEntry?.IsPosted ?? false,
                invoice.CreatedAt,
                invoice.Items
                    .OrderBy(item => item.SortOrder)
                    .Select(item => new InvoiceItemDto(
                        item.Id,
                        item.Description,
                        item.Quantity,
                        item.UnitPrice,
                        item.DiscountAmount,
                        item.TaxRate,
                        item.LineTotal,
                        item.SortOrder))
                    .ToList(),
                invoice.JournalEntry?.Lines
                    .OrderBy(line => line.Id)
                    .Select(line => new InvoiceJournalEntryLineDto(
                        line.Id,
                        line.AccountId,
                        line.Account.Code,
                        line.Account.Name,
                        line.Debit,
                        line.Credit,
                        line.Notes))
                    .ToList() ?? new List<InvoiceJournalEntryLineDto>());
        }

        private async Task<InvoiceDetailDto> MapDetailAsync(int invoiceId)
        {
            var invoice = await _context.Invoices
                .AsNoTracking()
                .Include(x => x.Items.OrderBy(item => item.SortOrder))
                .Include(x => x.Traveler)
                .Include(x => x.Trip)
                    .ThenInclude(x => x!.Traveler)
                .Include(x => x.Currency)
                .Include(x => x.CostCenter)
                .Include(x => x.PaymentTerm)
                .Include(x => x.JournalEntry)
                    .ThenInclude(x => x!.Lines.OrderBy(line => line.Id))
                    .ThenInclude(x => x.Account)
                .FirstAsync(x => x.Id == invoiceId);

            return MapDetail(invoice);
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

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.Invoices");
        }
    }
}
