using RowadUmrahSystem.Web.Models.Accounting;

namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record ExpenseListItemDto(
        int Id,
        DateTime ExpenseDate,
        string Category,
        string Title,
        decimal Amount,
        PaymentMethod PaymentMethod,
        int? BankAccountId,
        string? BankAccountName,
        int? TripId,
        string? TripLabel,
        int? JournalEntryId,
        string? JournalEntryNumber,
        DateTime CreatedAt);

    public sealed record ExpenseDetailDto(
        int Id,
        DateTime ExpenseDate,
        string Category,
        string Title,
        decimal Amount,
        PaymentMethod PaymentMethod,
        int? BankAccountId,
        string? BankAccountName,
        int? TripId,
        string? TripLabel,
        int? JournalEntryId,
        string? JournalEntryNumber,
        DateTime? JournalEntryDate,
        string? JournalEntryDescription,
        bool JournalEntryIsPosted,
        string Notes,
        DateTime CreatedAt);

    public sealed record ExpenseLookupsDto(
        IReadOnlyList<LookupOptionDto> BankAccounts,
        IReadOnlyList<LookupOptionDto> Trips);

    public sealed class ExpenseUpsertRequestDto
    {
        public DateTime ExpenseDate { get; set; } = DateTime.Now;

        public string Category { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public int? BankAccountId { get; set; }

        public int? TripId { get; set; }

        public string Notes { get; set; } = string.Empty;
    }
}
