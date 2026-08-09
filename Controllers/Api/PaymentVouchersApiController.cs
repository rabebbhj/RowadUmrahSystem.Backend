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
    [Route("api/payment-vouchers")]
    public class PaymentVouchersApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public PaymentVouchersApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<PaymentVoucherListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? bankAccountId = null)
        {
            if (!await CanAccess())
                return Forbid();

            var query = _context.PaymentVouchers
                .AsNoTracking()
                .Include(x => x.BankAccount)
                .Include(x => x.JournalEntry)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.VoucherNumber.Contains(search) ||
                    x.PaidTo.Contains(search) ||
                    x.Description.Contains(search));
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
                .Select(x => new PaymentVoucherListItemDto(
                    x.Id,
                    x.VoucherNumber,
                    x.VoucherDate,
                    x.PaidTo,
                    x.Amount,
                    x.PaymentMethod,
                    x.BankAccountId,
                    x.BankAccount != null ? x.BankAccount.BankName : null,
                    x.JournalEntryId,
                    x.JournalEntry != null ? x.JournalEntry.EntryNumber : null,
                    x.CreatedAt))
                .ToListAsync();

            return Ok(vouchers);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<PaymentVoucherDetailDto>> GetById(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var voucher = await _context.PaymentVouchers
                .AsNoTracking()
                .Include(x => x.BankAccount)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (voucher == null)
                return NotFound();

            return Ok(MapDetail(voucher));
        }

        [HttpGet("lookups")]
        public async Task<ActionResult<PaymentVoucherLookupsDto>> GetLookups()
        {
            if (!await CanAccess())
                return Forbid();

            var bankAccounts = await _context.BankAccounts
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.BankName)
                .Select(x => new LookupOptionDto(
                    x.Id,
                    x.BankName + (x.IsCashBox ? " (Cash box)" : string.Empty)))
                .ToListAsync();

            return Ok(new PaymentVoucherLookupsDto(bankAccounts));
        }

        [HttpPost]
        public async Task<ActionResult<PaymentVoucherDetailDto>> Create([FromBody] PaymentVoucherUpsertRequestDto request)
        {
            if (!await CanAccess())
                return Forbid();

            var validation = await ValidateAsync(request);
            if (validation != null)
                return BadRequest(validation);

            var bankAccount = await _context.BankAccounts
                .AsNoTracking()
                .FirstAsync(x => x.Id == request.BankAccountId!.Value && x.IsActive);

            var voucher = new PaymentVoucher
            {
                VoucherNumber = await GenerateVoucherNumber(),
                VoucherDate = request.VoucherDate,
                PaidTo = request.PaidTo.Trim(),
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod,
                BankAccountId = bankAccount.Id,
                Description = request.Description?.Trim() ?? string.Empty,
                CreatedAt = DateTime.Now
            };

            _context.PaymentVouchers.Add(voucher);
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(voucher.Id));
        }

        private async Task<string?> ValidateAsync(PaymentVoucherUpsertRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.PaidTo))
                return "Paid to is required.";

            if (request.Amount <= 0)
                return "Payment amount must be greater than zero.";

            if (!Enum.IsDefined(typeof(PaymentMethod), request.PaymentMethod))
                return "Invalid payment method.";

            if (!request.BankAccountId.HasValue)
                return "Bank account is required.";

            var bankAccountExists = await _context.BankAccounts
                .AnyAsync(x => x.Id == request.BankAccountId.Value && x.IsActive);

            if (!bankAccountExists)
                return "Selected bank account is not valid or active.";

            return null;
        }

        private PaymentVoucherDetailDto MapDetail(PaymentVoucher voucher)
        {
            return new PaymentVoucherDetailDto(
                voucher.Id,
                voucher.VoucherNumber,
                voucher.VoucherDate,
                voucher.PaidTo,
                voucher.Amount,
                voucher.PaymentMethod,
                voucher.BankAccountId,
                voucher.BankAccount?.BankName,
                voucher.Description,
                voucher.JournalEntryId,
                voucher.JournalEntry?.EntryNumber,
                voucher.JournalEntry?.EntryDate,
                voucher.JournalEntry?.Description,
                voucher.JournalEntry?.IsPosted ?? false,
                voucher.CreatedAt);
        }

        private async Task<PaymentVoucherDetailDto> MapDetailAsync(int id)
        {
            var voucher = await _context.PaymentVouchers
                .AsNoTracking()
                .Include(x => x.BankAccount)
                .Include(x => x.JournalEntry)
                .FirstAsync(x => x.Id == id);

            return MapDetail(voucher);
        }

        private async Task<string> GenerateVoucherNumber()
        {
            var year = DateTime.Now.Year;
            var prefix = $"PV-{year}-";

            var count = await _context.PaymentVouchers
                .AsNoTracking()
                .CountAsync(x => x.VoucherNumber.StartsWith(prefix));

            return $"{prefix}{count + 1:00000}";
        }

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.PaymentVouchers");
        }
    }
}
