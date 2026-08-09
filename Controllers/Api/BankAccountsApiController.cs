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
    [Route("api/bank-accounts")]
    public class BankAccountsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public BankAccountsApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<BankAccountListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] bool? isActive = null)
        {
            if (!await CanViewBankAccounts())
                return Forbid();

            var query = _context.BankAccounts
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.BankName.Contains(search) ||
                    x.AccountNumber.Contains(search) ||
                    x.Iban.Contains(search));
            }

            if (isActive.HasValue)
            {
                query = query.Where(x => x.IsActive == isActive.Value);
            }

            var items = await query
                .OrderBy(x => x.BankName)
                .Select(x => new BankAccountListItemDto(
                    x.Id,
                    x.BankName,
                    x.AccountNumber,
                    x.Iban,
                    x.OpeningBalance,
                    x.IsCashBox,
                    x.IsActive,
                    x.CreatedAt,
                    _context.BankTransactions.Count(t => t.BankAccountId == x.Id)))
                .ToListAsync();

            return Ok(items);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<BankAccountDetailDto>> GetById(int id)
        {
            if (!await CanViewBankAccounts())
                return Forbid();

            var account = await _context.BankAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (account == null)
                return NotFound();

            return Ok(await MapDetailAsync(account.Id));
        }

        [HttpPost]
        public async Task<ActionResult<BankAccountDetailDto>> Create([FromBody] BankAccountUpsertRequestDto request)
        {
            if (!await CanManageBankAccounts())
                return Forbid();

            var validation = Validate(request);
            if (validation != null)
                return validation;

            var bankAccount = new BankAccount
            {
                BankName = request.BankName.Trim(),
                AccountNumber = request.AccountNumber?.Trim() ?? string.Empty,
                Iban = request.Iban?.Trim() ?? string.Empty,
                OpeningBalance = request.OpeningBalance,
                IsCashBox = request.IsCashBox,
                IsActive = request.IsActive,
                CreatedAt = DateTime.Now
            };

            _context.BankAccounts.Add(bankAccount);
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(bankAccount.Id));
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<BankAccountDetailDto>> Update(int id, [FromBody] BankAccountUpsertRequestDto request)
        {
            if (!await CanManageBankAccounts())
                return Forbid();

            var bankAccount = await _context.BankAccounts.FirstOrDefaultAsync(x => x.Id == id);
            if (bankAccount == null)
                return NotFound();

            var validation = Validate(request);
            if (validation != null)
                return validation;

            bankAccount.BankName = request.BankName.Trim();
            bankAccount.AccountNumber = request.AccountNumber?.Trim() ?? string.Empty;
            bankAccount.Iban = request.Iban?.Trim() ?? string.Empty;
            bankAccount.OpeningBalance = request.OpeningBalance;
            bankAccount.IsCashBox = request.IsCashBox;
            bankAccount.IsActive = request.IsActive;

            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(bankAccount.Id));
        }

        [HttpPost("{id:int}/toggle-status")]
        public async Task<ActionResult<BankAccountDetailDto>> ToggleStatus(int id)
        {
            if (!await CanManageBankAccounts())
                return Forbid();

            var bankAccount = await _context.BankAccounts.FirstOrDefaultAsync(x => x.Id == id);
            if (bankAccount == null)
                return NotFound();

            bankAccount.IsActive = !bankAccount.IsActive;
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(bankAccount.Id));
        }

        private static ActionResult? Validate(BankAccountUpsertRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.BankName))
                return new BadRequestObjectResult("Bank name is required.");

            return null;
        }

        private async Task<BankAccountDetailDto> MapDetailAsync(int id)
        {
            var account = await _context.BankAccounts
                .AsNoTracking()
                .FirstAsync(x => x.Id == id);

            var transactions = await _context.BankTransactions
                .AsNoTracking()
                .Include(x => x.JournalEntry)
                .Where(x => x.BankAccountId == id)
                .OrderByDescending(x => x.TransactionDate)
                .Take(20)
                .ToListAsync();

            return new BankAccountDetailDto(
                account.Id,
                account.BankName,
                account.AccountNumber,
                account.Iban,
                account.OpeningBalance,
                account.IsCashBox,
                account.IsActive,
                account.CreatedAt,
                transactions.Count,
                transactions.Select(transaction => new BankTransactionDto(
                        transaction.Id,
                        transaction.TransactionDate,
                        transaction.TransactionType,
                        transaction.Amount,
                        transaction.ReferenceNumber,
                        transaction.Description,
                        transaction.JournalEntryId,
                        transaction.JournalEntry?.EntryNumber))
                    .ToList());
        }

        private async Task<bool> CanViewBankAccounts()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.Banks") ||
                   await _permissionService.HasPermissionAsync(User, "Accounting.Manage");
        }

        private async Task<bool> CanManageBankAccounts()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.Manage") ||
                   await _permissionService.HasPermissionAsync(User, "Accounting.Banks");
        }
    }
}
