using RowadUmrahSystem.Web.Models.Accounting;

namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record PaymentVoucherListItemDto(
        int Id,
        string VoucherNumber,
        DateTime VoucherDate,
        string PaidTo,
        decimal Amount,
        PaymentMethod PaymentMethod,
        int? BankAccountId,
        string? BankAccountName,
        int? JournalEntryId,
        string? JournalEntryNumber,
        DateTime CreatedAt);

    public sealed record PaymentVoucherDetailDto(
        int Id,
        string VoucherNumber,
        DateTime VoucherDate,
        string PaidTo,
        decimal Amount,
        PaymentMethod PaymentMethod,
        int? BankAccountId,
        string? BankAccountName,
        string Description,
        int? JournalEntryId,
        string? JournalEntryNumber,
        DateTime? JournalEntryDate,
        string? JournalEntryDescription,
        bool JournalEntryIsPosted,
        DateTime CreatedAt);

    public sealed record PaymentVoucherLookupsDto(
        IReadOnlyList<LookupOptionDto> BankAccounts);

    public sealed class PaymentVoucherUpsertRequestDto
    {
        public DateTime VoucherDate { get; set; } = DateTime.Now;

        public string PaidTo { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public int? BankAccountId { get; set; }

        public string Description { get; set; } = string.Empty;
    }
}
