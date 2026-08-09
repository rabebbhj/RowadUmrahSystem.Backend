using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Models.Accounting;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.ViewModels.Api;

namespace RowadUmrahSystem.Web.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/expenses")]
    public class ExpensesApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public ExpensesApiController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ExpenseListItemDto>>> GetAll(
            [FromQuery] string? search = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? bankAccountId = null)
        {
            if (!await CanAccess())
                return Forbid();

            var query = _context.Expenses
                .AsNoTracking()
                .Include(x => x.BankAccount)
                .Include(x => x.Trip)
                    .ThenInclude(x => x!.Traveler)
                .Include(x => x.JournalEntry)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.Category.Contains(search) ||
                    x.Title.Contains(search) ||
                    x.Notes.Contains(search) ||
                    (x.BankAccount != null && x.BankAccount.BankName.Contains(search)) ||
                    (x.Trip != null && x.Trip.Traveler.FullName.Contains(search)) ||
                    (x.JournalEntry != null && x.JournalEntry.EntryNumber.Contains(search)));
            }

            if (fromDate.HasValue)
            {
                query = query.Where(x => x.ExpenseDate.Date >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                query = query.Where(x => x.ExpenseDate.Date <= toDate.Value.Date);
            }

            if (bankAccountId.HasValue)
            {
                query = query.Where(x => x.BankAccountId == bankAccountId.Value);
            }

            var expenses = await query
                .OrderByDescending(x => x.ExpenseDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return Ok(expenses.Select(MapListItem).ToList());
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ExpenseDetailDto>> GetById(int id)
        {
            if (!await CanAccess())
                return Forbid();

            var expense = await _context.Expenses
                .AsNoTracking()
                .Include(x => x.BankAccount)
                .Include(x => x.Trip)
                    .ThenInclude(x => x!.Traveler)
                .Include(x => x.JournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (expense == null)
                return NotFound();

            return Ok(MapDetail(expense));
        }

        [HttpGet("lookups")]
        public async Task<ActionResult<ExpenseLookupsDto>> GetLookups()
        {
            if (!await CanAccess())
                return Forbid();

            var bankAccounts = await _context.BankAccounts
                .AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.BankName)
                .ToListAsync();

            var trips = await _context.Trips
                .AsNoTracking()
                .Include(x => x.Traveler)
                .Where(x => !x.IsDeleted)
                .OrderByDescending(x => x.TripDate)
                .ToListAsync();

            return Ok(new ExpenseLookupsDto(
                bankAccounts.Select(x => new LookupOptionDto(
                    x.Id,
                    x.BankName + (x.IsCashBox ? " (Cash box)" : string.Empty))).ToList(),
                trips.Select(x => new LookupOptionDto(
                    x.Id,
                    BuildTripLabel(x)!)).ToList()));
        }

        [HttpPost]
        public async Task<ActionResult<ExpenseDetailDto>> Create([FromBody] ExpenseUpsertRequestDto request)
        {
            if (!await CanAccess())
                return Forbid();

            var validation = await ValidateAsync(request);
            if (validation != null)
                return BadRequest(validation);

            var expense = new Expense
            {
                ExpenseDate = request.ExpenseDate,
                Category = request.Category.Trim(),
                Title = request.Title.Trim(),
                Amount = request.Amount,
                PaymentMethod = request.PaymentMethod,
                BankAccountId = request.BankAccountId,
                TripId = request.TripId,
                Notes = request.Notes?.Trim() ?? string.Empty,
                CreatedAt = DateTime.Now
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();

            return Ok(await MapDetailAsync(expense.Id));
        }

        private ExpenseListItemDto MapListItem(Expense expense)
        {
            return new ExpenseListItemDto(
                expense.Id,
                expense.ExpenseDate,
                expense.Category,
                expense.Title,
                expense.Amount,
                expense.PaymentMethod,
                expense.BankAccountId,
                BuildBankAccountLabel(expense.BankAccount),
                expense.TripId,
                BuildTripLabel(expense.Trip),
                expense.JournalEntryId,
                expense.JournalEntry?.EntryNumber,
                expense.CreatedAt);
        }

        private ExpenseDetailDto MapDetail(Expense expense)
        {
            return new ExpenseDetailDto(
                expense.Id,
                expense.ExpenseDate,
                expense.Category,
                expense.Title,
                expense.Amount,
                expense.PaymentMethod,
                expense.BankAccountId,
                BuildBankAccountLabel(expense.BankAccount),
                expense.TripId,
                BuildTripLabel(expense.Trip),
                expense.JournalEntryId,
                expense.JournalEntry?.EntryNumber,
                expense.JournalEntry?.EntryDate,
                expense.JournalEntry?.Description,
                expense.JournalEntry?.IsPosted ?? false,
                expense.Notes,
                expense.CreatedAt);
        }

        private async Task<ExpenseDetailDto> MapDetailAsync(int id)
        {
            var expense = await _context.Expenses
                .AsNoTracking()
                .Include(x => x.BankAccount)
                .Include(x => x.Trip)
                    .ThenInclude(x => x!.Traveler)
                .Include(x => x.JournalEntry)
                .FirstAsync(x => x.Id == id);

            return MapDetail(expense);
        }

        private static string? BuildBankAccountLabel(BankAccount? bankAccount)
        {
            if (bankAccount == null)
                return null;

            return bankAccount.BankName + (bankAccount.IsCashBox ? " (Cash box)" : string.Empty);
        }

        private static string? BuildTripLabel(Trip? trip)
        {
            if (trip == null)
                return null;

            var travelerName = trip.Traveler?.FullName ?? "Trip";
            return $"{travelerName} - {trip.TripType} - {trip.TripDate:yyyy-MM-dd}";
        }

        private async Task<string?> ValidateAsync(ExpenseUpsertRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Category))
                return "Category is required.";

            if (string.IsNullOrWhiteSpace(request.Title))
                return "Title is required.";

            if (request.Amount <= 0)
                return "Expense amount must be greater than zero.";

            if (!Enum.IsDefined(typeof(PaymentMethod), request.PaymentMethod))
                return "Invalid payment method.";

            if (request.BankAccountId.HasValue)
            {
                var bankAccountExists = await _context.BankAccounts
                    .AnyAsync(x => x.Id == request.BankAccountId.Value && x.IsActive);

                if (!bankAccountExists)
                    return "Selected bank account is not valid or active.";
            }

            if (request.TripId.HasValue)
            {
                var tripExists = await _context.Trips
                    .AnyAsync(x => x.Id == request.TripId.Value && !x.IsDeleted);

                if (!tripExists)
                    return "Selected trip is not valid or active.";
            }

            return null;
        }

        private async Task<bool> CanAccess()
        {
            return await _permissionService.HasPermissionAsync(User, "Accounting.Expenses");
        }
    }
}
