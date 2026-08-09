using ClosedXML.Excel;
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
    [Route("api/accounts")]
    public class AccountsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public AccountsApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<AccountListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] AccountType? type = null,
            [FromQuery] bool? isActive = null)
        {
            if (!await CanAccess())
                return Forbid();

            var query = _context.Accounts
                .AsNoTracking()
                .Include(x => x.ParentAccount)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.Code.Contains(search) ||
                    x.Name.Contains(search));
            }

            if (type.HasValue)
            {
                query = query.Where(x => x.Type == type.Value);
            }

            if (isActive.HasValue)
            {
                query = query.Where(x => x.IsActive == isActive.Value);
            }

            var accounts = await query
                .OrderBy(x => x.Code)
                .Select(x => new AccountListItemDto(
                    x.Id,
                    x.Code,
                    x.Name,
                    x.Type,
                    x.ParentAccountId,
                    x.ParentAccount != null ? x.ParentAccount.Name : null,
                    x.IsActive,
                    x.IsSystemAccount,
                    x.CreatedAt))
                .ToListAsync();

            return Ok(accounts);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<AccountDetailDto>> GetById(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var account = await _context.Accounts
                .AsNoTracking()
                .Include(x => x.ParentAccount)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (account == null)
                return NotFound();

            return Ok(await MapDetailAsync(account.Id));
        }

        [HttpPost]
        public async Task<ActionResult<AccountDetailDto>> Create([FromBody] AccountUpsertRequestDto request)
        {
            if (!await CanAccess())
                return Forbid();

            var validation = await ValidateAsync(request);
            if (validation != null)
                return validation;

            var account = new Account
            {
                Code = request.Code.Trim(),
                Name = request.Name.Trim(),
                Type = request.Type,
                ParentAccountId = request.ParentAccountId,
                IsActive = request.IsActive,
                IsSystemAccount = false,
                CreatedAt = DateTime.Now
            };

            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(account.Id));
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<AccountDetailDto>> Update(int id, [FromBody] AccountUpsertRequestDto request)
        {
            if (!await CanAccess())
                return Forbid();

            var account = await _context.Accounts.FirstOrDefaultAsync(x => x.Id == id);
            if (account == null)
                return NotFound();

            var validation = await ValidateAsync(request, id);
            if (validation != null)
                return validation;

            if (account.IsSystemAccount && !string.Equals(account.Code, request.Code.Trim(), StringComparison.Ordinal))
            {
                return BadRequest("Cannot change the code of a system account.");
            }

            account.Code = request.Code.Trim();
            account.Name = request.Name.Trim();
            account.Type = request.Type;
            account.ParentAccountId = request.ParentAccountId;
            account.IsActive = request.IsActive;

            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(account.Id));
        }

        [HttpPost("{id:int}/toggle-status")]
        public async Task<ActionResult<AccountDetailDto>> ToggleStatus(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var account = await _context.Accounts.FirstOrDefaultAsync(x => x.Id == id);
            if (account == null)
                return NotFound();

            if (account.IsSystemAccount)
                return BadRequest("Cannot disable a system account.");

            account.IsActive = !account.IsActive;
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(account.Id));
        }

        [HttpGet("export")]
        public async Task<IActionResult> Export()
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

            worksheet.Cell(1, 1).Value = "Code";
            worksheet.Cell(1, 2).Value = "Name";
            worksheet.Cell(1, 3).Value = "Type";
            worksheet.Cell(1, 4).Value = "Parent";
            worksheet.Cell(1, 5).Value = "Status";
            worksheet.Cell(1, 6).Value = "System";

            var row = 2;
            foreach (var account in accounts)
            {
                worksheet.Cell(row, 1).Value = account.Code;
                worksheet.Cell(row, 2).Value = account.Name;
                worksheet.Cell(row, 3).Value = GetTypeName(account.Type);
                worksheet.Cell(row, 4).Value = account.ParentAccount?.Name ?? "-";
                worksheet.Cell(row, 5).Value = account.IsActive ? "Active" : "Inactive";
                worksheet.Cell(row, 6).Value = account.IsSystemAccount ? "Yes" : "No";
                row++;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"ChartOfAccounts_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
        }

        private async Task<ActionResult?> ValidateAsync(AccountUpsertRequestDto request, int? currentAccountId = null)
        {
            var code = request.Code?.Trim() ?? string.Empty;
            var name = request.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(code))
                return BadRequest("Account code is required.");

            if (string.IsNullOrWhiteSpace(name))
                return BadRequest("Account name is required.");

            var duplicateCode = await _context.Accounts.AnyAsync(x =>
                x.Code == code && x.Id != currentAccountId);

            if (duplicateCode)
                return Conflict("Account code already exists.");

            if (request.ParentAccountId.HasValue)
            {
                var parent = await _context.Accounts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == request.ParentAccountId.Value);

                if (parent == null)
                    return BadRequest("Parent account not found.");

                if (currentAccountId.HasValue && parent.Id == currentAccountId.Value)
                    return BadRequest("An account cannot be its own parent.");

                if (parent.Type != request.Type)
                    return BadRequest("Parent account type must match the child account type.");
            }

            return null;
        }

        private async Task<AccountDetailDto> MapDetailAsync(int accountId)
        {
            var account = await _context.Accounts
                .AsNoTracking()
                .Include(x => x.ParentAccount)
                .FirstAsync(x => x.Id == accountId);

            var lines = await _context.JournalEntryLines
                .AsNoTracking()
                .Include(x => x.JournalEntry)
                .Where(x => x.AccountId == account.Id)
                .OrderByDescending(x => x.JournalEntry.EntryDate)
                .Take(100)
                .ToListAsync();

            var totalDebit = lines.Sum(x => x.Debit);
            var totalCredit = lines.Sum(x => x.Credit);

            return new AccountDetailDto(
                account.Id,
                account.Code,
                account.Name,
                account.Type,
                account.ParentAccountId,
                account.ParentAccount?.Name,
                account.IsActive,
                account.IsSystemAccount,
                account.CreatedAt,
                totalDebit,
                totalCredit,
                totalDebit - totalCredit,
                lines.Select(line => new AccountJournalLineDto(
                        line.Id,
                        line.JournalEntry.EntryDate,
                        line.JournalEntry.EntryNumber,
                        line.JournalEntry.Description,
                        line.Debit,
                        line.Credit,
                        line.Notes))
                    .ToList());
        }

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.ChartOfAccounts");
        }

        private static string GetTypeName(AccountType type)
        {
            return type switch
            {
                AccountType.Asset => "Asset",
                AccountType.Liability => "Liability",
                AccountType.Equity => "Equity",
                AccountType.Revenue => "Revenue",
                AccountType.Expense => "Expense",
                _ => "Unknown"
            };
        }
    }
}
