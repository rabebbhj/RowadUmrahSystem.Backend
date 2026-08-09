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
    [Route("api/receipt-vouchers")]
    public class ReceiptVouchersApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public ReceiptVouchersApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ReceiptVoucherListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? bankAccountId = null)
        {
            if (!await CanAccess())
                return Forbid();

            var query = _context.ReceiptVouchers
                .AsNoTracking()
                .Include(x => x.Invoice)
                .Include(x => x.BankAccount)
                .Include(x => x.JournalEntry)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.VoucherNumber.Contains(search) ||
                    x.ReceivedFrom.Contains(search) ||
                    x.Description.Contains(search) ||
                    (x.Invoice != null && x.Invoice.InvoiceNumber.Contains(search)));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(x => x.VoucherDate.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(x => x.VoucherDate.Date <= toDate.Value.Date);
            }

            if (bankAccountId.HasValue)
            {
                query = query.Where(x => x.BankAccountId == bankAccountId.Value);
            }

            var vouchers = await query
                .OrderByDescending(x => x.VoucherDate)
                .ThenByDescending(x => x.Id)
                .Select(x => new ReceiptVoucherListItemDto(
                    x.Id,
                    x.VoucherNumber,
                    x.VoucherDate,
                    x.ReceivedFrom,
                    x.Amount,
                    x.PaymentMethod,
                    x.InvoiceId,
                    x.Invoice != null ? x.Invoice.InvoiceNumber : null,
                    x.Invoice != null ? x.Invoice.CustomerName : null,
                    x.BankAccountId,
                    x.BankAccount != null ? x.BankAccount.BankName : null,
                    x.JournalEntryId,
                    x.JournalEntry != null ? x.JournalEntry.EntryNumber : null,
                    x.CreatedAt))
                .ToListAsync();

            return Ok(vouchers);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ReceiptVoucherDetailDto>> GetById(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var voucher = await _context.ReceiptVouchers
                .AsNoTracking()
                .Include(x => x.Invoice)
                .Include(x => x.BankAccount)
                .Include(x => x.JournalEntry)
                    .ThenInclude(x => x!.Lines)
                    .ThenInclude(x => x.Account)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (voucher == null)
                return NotFound();

            return Ok(MapDetail(voucher));
        }

        [HttpGet("lookups")]
        public async Task<ActionResult<ReceiptVoucherLookupsDto>> GetLookups()
        {
            if (!await CanAccess())
                return Forbid();

            var invoices = await _context.Invoices
                .AsNoTracking()
                .Where(x => !x.IsArchived && x.Status != InvoiceStatus.Cancelled && x.TotalAmount > x.PaidAmount)
                .OrderByDescending(x => x.InvoiceDate)
                .Select(x => new LookupOptionDto(
                    x.Id,
                    $"{x.InvoiceNumber} - {x.CustomerName} - Remaining: {(x.TotalAmount - x.PaidAmount).ToString("N3")}"))
                .ToListAsync();

            var bankAccounts = await _context.BankAccounts
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.BankName)
                .Select(x => new LookupOptionDto(
                    x.Id,
                    x.BankName + (x.IsCashBox ? " (Cash box)" : string.Empty)))
                .ToListAsync();

            return Ok(new ReceiptVoucherLookupsDto(invoices, bankAccounts));
        }

        [HttpPost]
        public async Task<ActionResult<ReceiptVoucherDetailDto>> Create([FromBody] ReceiptVoucherUpsertRequestDto request)
        {
            if (!await CanAccess())
                return Forbid();

            var validation = await ValidateAsync(request);
            if (validation != null)
                return BadRequest(validation);

            var invoice = await _context.Invoices
                .FirstAsync(x => x.Id == request.InvoiceId!.Value);

            var bankAccount = await _context.BankAccounts
                .AsNoTracking()
                .FirstAsync(x => x.Id == request.BankAccountId!.Value && x.IsActive);

            await using var transaction = await _context.Database.BeginTransactionAsync();

            var voucherNumber = await GenerateVoucherNumber();
            var receiptVoucher = new ReceiptVoucher
            {
                VoucherNumber = voucherNumber,
                VoucherDate = request.VoucherDate,
                ReceivedFrom = request.ReceivedFrom.Trim(),
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod,
                InvoiceId = invoice.Id,
                BankAccountId = bankAccount.Id,
                Description = request.Description?.Trim() ?? string.Empty,
                CreatedAt = DateTime.Now
            };

            _context.ReceiptVouchers.Add(receiptVoucher);

            invoice.PaidAmount += request.Amount;
            if (invoice.PaidAmount >= invoice.TotalAmount)
            {
                invoice.PaidAmount = invoice.TotalAmount;
                invoice.Status = InvoiceStatus.Paid;
                invoice.PaidAt = DateTime.Now;
            }
            else
            {
                invoice.Status = InvoiceStatus.PartiallyPaid;
                invoice.PaidAt = null;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(await MapDetailAsync(receiptVoucher.Id));
        }

        private async Task<string?> ValidateAsync(ReceiptVoucherUpsertRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.ReceivedFrom))
                return "Received from is required.";

            if (request.Amount <= 0)
                return "Receipt amount must be greater than zero.";

            if (!Enum.IsDefined(typeof(PaymentMethod), request.PaymentMethod))
                return "Invalid payment method.";

            if (!request.InvoiceId.HasValue)
                return "Receipt voucher must be linked to an invoice.";

            var invoice = await _context.Invoices
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.InvoiceId.Value);

            if (invoice == null)
                return "Invoice not found.";

            if (invoice.Status == InvoiceStatus.Cancelled)
                return "Cannot create a receipt voucher for a cancelled invoice.";

            var remaining = invoice.TotalAmount - invoice.PaidAmount;
            if (request.Amount > remaining)
                return "Receipt amount exceeds the invoice remaining balance.";

            if (!request.BankAccountId.HasValue)
                return "Bank account is required.";

            var bankAccountExists = await _context.BankAccounts
                .AnyAsync(x => x.Id == request.BankAccountId.Value && x.IsActive);

            if (!bankAccountExists)
                return "Selected bank account is not valid or active.";

            return null;
        }

        private ReceiptVoucherDetailDto MapDetail(ReceiptVoucher voucher)
        {
            var journalLines = voucher.JournalEntry?.Lines
                .OrderBy(x => x.Id)
                .Select(x => new ReceiptVoucherJournalLineDto(
                    x.Id,
                    x.AccountId,
                    x.Account.Code,
                    x.Account.Name,
                    x.Debit,
                    x.Credit,
                    x.Notes))
                .ToList() ?? new List<ReceiptVoucherJournalLineDto>();

            return new ReceiptVoucherDetailDto(
                voucher.Id,
                voucher.VoucherNumber,
                voucher.VoucherDate,
                voucher.ReceivedFrom,
                voucher.Amount,
                voucher.PaymentMethod,
                voucher.InvoiceId,
                voucher.Invoice?.InvoiceNumber,
                voucher.Invoice?.CustomerName,
                voucher.Invoice?.TotalAmount,
                voucher.Invoice?.PaidAmount,
                voucher.Invoice != null ? voucher.Invoice.TotalAmount - voucher.Invoice.PaidAmount : null,
                voucher.BankAccountId,
                voucher.BankAccount?.BankName,
                voucher.Description,
                voucher.JournalEntryId,
                voucher.JournalEntry?.EntryNumber,
                voucher.JournalEntry?.EntryDate,
                voucher.JournalEntry?.Description,
                voucher.JournalEntry?.IsPosted ?? false,
                voucher.CreatedAt,
                journalLines);
        }

        private async Task<ReceiptVoucherDetailDto> MapDetailAsync(int id)
        {
            var voucher = await _context.ReceiptVouchers
                .AsNoTracking()
                .Include(x => x.Invoice)
                .Include(x => x.BankAccount)
                .Include(x => x.JournalEntry)
                    .ThenInclude(x => x!.Lines)
                    .ThenInclude(x => x.Account)
                .FirstAsync(x => x.Id == id);

            return MapDetail(voucher);
        }

        private async Task<string> GenerateVoucherNumber()
        {
            var year = DateTime.Now.Year;
            var prefix = $"RV-{year}-";

            var count = await _context.ReceiptVouchers
                .AsNoTracking()
                .CountAsync(x => x.VoucherNumber.StartsWith(prefix));

            return $"{prefix}{count + 1:00000}";
        }

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.ReceiptVouchers");
        }
    }
}
