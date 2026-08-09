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
    [Route("api/journal-entries")]
    public class JournalEntriesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public JournalEntriesApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<JournalEntryListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] bool? isPosted = null)
        {
            if (!await CanAccess())
                return Forbid();

            var query = _context.JournalEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.EntryNumber.Contains(search) ||
                    x.Description.Contains(search) ||
                    x.SourceType.Contains(search));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(x => x.EntryDate.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(x => x.EntryDate.Date <= toDate.Value.Date);
            }

            if (isPosted.HasValue)
            {
                query = query.Where(x => x.IsPosted == isPosted.Value);
            }

            var entries = await query
                .OrderByDescending(x => x.EntryDate)
                .ThenByDescending(x => x.Id)
                .Select(x => new JournalEntryListItemDto(
                    x.Id,
                    x.EntryNumber,
                    x.EntryDate,
                    x.Description,
                    x.SourceType,
                    x.IsPosted,
                    x.Lines.Sum(line => line.Debit),
                    x.Lines.Sum(line => line.Credit),
                    x.Lines.Sum(line => line.Debit) - x.Lines.Sum(line => line.Credit),
                    x.Lines.Count,
                    x.CreatedAt))
                .ToListAsync();

            return Ok(entries);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<JournalEntryDetailDto>> GetById(int id)
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

            return Ok(MapDetail(entry));
        }

        [HttpGet("lookups")]
        public async Task<ActionResult<JournalEntryLookupsDto>> GetLookups()
        {
            if (!await CanAccess())
                return Forbid();

            var accounts = await _context.Accounts
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => new LookupOptionDto(x.Id, x.Code + " - " + x.Name))
                .ToListAsync();

            return Ok(new JournalEntryLookupsDto(accounts));
        }

        [HttpPost]
        public async Task<ActionResult<JournalEntryDetailDto>> Create([FromBody] JournalEntryUpsertRequestDto request)
        {
            if (!await CanAccess())
                return Forbid();

            var validation = await ValidateAsync(request);
            if (validation != null)
                return BadRequest(validation);

            var lines = NormalizeLines(request.Lines);
            var entryNumber = await GenerateEntryNumber();
            var entry = new JournalEntry
            {
                EntryNumber = entryNumber,
                EntryDate = request.EntryDate,
                Description = request.Description?.Trim() ?? string.Empty,
                SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "Manual" : request.SourceType.Trim(),
                IsPosted = true,
                CreatedAt = DateTime.Now,
                Lines = lines.Select(line => new JournalEntryLine
                {
                    AccountId = line.AccountId,
                    Debit = line.Debit,
                    Credit = line.Credit,
                    Notes = line.Notes?.Trim() ?? string.Empty
                }).ToList()
            };

            _context.JournalEntries.Add(entry);
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(entry.Id));
        }

        [HttpPost("{id:int}/toggle-posted")]
        public async Task<ActionResult<JournalEntryDetailDto>> TogglePosted(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var entry = await _context.JournalEntries
                .Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (entry == null)
                return NotFound();

            var totalDebit = entry.Lines.Sum(x => x.Debit);
            var totalCredit = entry.Lines.Sum(x => x.Credit);

            if (!entry.Lines.Any() || totalDebit != totalCredit)
                return BadRequest("لا يمكن ترحيل قيد غير متوازن.");

            entry.IsPosted = !entry.IsPosted;
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(entry.Id));
        }

        private async Task<string?> ValidateAsync(JournalEntryUpsertRequestDto request)
        {
            var lines = NormalizeLines(request.Lines);

            if (lines.Count < 2)
                return "يجب إدخال سطرين على الأقل داخل القيد.";

            foreach (var line in lines)
            {
                if (line.Debit > 0 && line.Credit > 0)
                    return "لا يمكن أن يكون السطر مدين ودائن في نفس الوقت.";

                if (line.Debit <= 0 && line.Credit <= 0)
                    return "كل سطر يجب أن يحتوي على مبلغ مدين أو دائن.";

                var accountExists = await _context.Accounts
                    .AnyAsync(x => x.Id == line.AccountId && x.IsActive);

                if (!accountExists)
                    return "يوجد حساب غير صحيح أو غير فعال داخل القيد.";
            }

            var totalDebit = lines.Sum(x => x.Debit);
            var totalCredit = lines.Sum(x => x.Credit);

            if (totalDebit != totalCredit)
                return "القيد غير متوازن. إجمالي المدين يجب أن يساوي إجمالي الدائن.";

            return null;
        }

        private static List<JournalEntryLineUpsertRequestDto> NormalizeLines(IEnumerable<JournalEntryLineUpsertRequestDto>? lines)
        {
            return (lines ?? Enumerable.Empty<JournalEntryLineUpsertRequestDto>())
                .Where(x => x.AccountId > 0 && (x.Debit > 0 || x.Credit > 0))
                .Select(x => new JournalEntryLineUpsertRequestDto
                {
                    AccountId = x.AccountId,
                    Debit = x.Debit,
                    Credit = x.Credit,
                    Notes = x.Notes?.Trim() ?? string.Empty
                })
                .ToList();
        }

        private JournalEntryDetailDto MapDetail(JournalEntry entry)
        {
            var totalDebit = entry.Lines.Sum(x => x.Debit);
            var totalCredit = entry.Lines.Sum(x => x.Credit);

            return new JournalEntryDetailDto(
                entry.Id,
                entry.EntryNumber,
                entry.EntryDate,
                entry.Description,
                entry.SourceType,
                entry.SourceId,
                entry.IsPosted,
                totalDebit,
                totalCredit,
                totalDebit - totalCredit,
                entry.CreatedAt,
                entry.Lines
                    .OrderBy(x => x.Id)
                    .Select(x => new JournalEntryLineDto(
                        x.Id,
                        x.AccountId,
                        x.Account.Code,
                        x.Account.Name,
                        x.Debit,
                        x.Credit,
                        x.Notes))
                    .ToList());
        }

        private async Task<JournalEntryDetailDto> MapDetailAsync(int id)
        {
            var entry = await _context.JournalEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                    .ThenInclude(x => x.Account)
                .FirstAsync(x => x.Id == id);

            return MapDetail(entry);
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
            return await _permissionService.HasPermissionAsync(User, "Accounting.JournalEntries");
        }
    }
}
