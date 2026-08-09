using RowadUmrahSystem.Web.Models.Accounting;

namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record ReceiptVoucherListItemDto(
        int Id,
        string VoucherNumber,
        DateTime VoucherDate,
        string ReceivedFrom,
        decimal Amount,
        PaymentMethod PaymentMethod,
        int? InvoiceId,
        string? InvoiceNumber,
        string? InvoiceCustomerName,
        int? BankAccountId,
        string? BankAccountName,
        int? JournalEntryId,
        string? JournalEntryNumber,
        DateTime CreatedAt);

    public sealed record ReceiptVoucherJournalLineDto(
        int Id,
        int AccountId,
        string AccountCode,
        string AccountName,
        decimal Debit,
        decimal Credit,
        string Notes);

    public sealed record ReceiptVoucherDetailDto(
        int Id,
        string VoucherNumber,
        DateTime VoucherDate,
        string ReceivedFrom,
        decimal Amount,
        PaymentMethod PaymentMethod,
        int? InvoiceId,
        string? InvoiceNumber,
        string? InvoiceCustomerName,
        decimal? InvoiceTotalAmount,
        decimal? InvoicePaidAmount,
        decimal? InvoiceRemainingAmount,
        int? BankAccountId,
        string? BankAccountName,
        string Description,
        int? JournalEntryId,
        string? JournalEntryNumber,
        DateTime? JournalEntryDate,
        string? JournalEntryDescription,
        bool JournalEntryIsPosted,
        DateTime CreatedAt,
        IReadOnlyList<ReceiptVoucherJournalLineDto> JournalEntryLines);

    public sealed record ReceiptVoucherLookupsDto(
        IReadOnlyList<LookupOptionDto> Invoices,
        IReadOnlyList<LookupOptionDto> BankAccounts);

    public sealed class ReceiptVoucherUpsertRequestDto
    {
        public DateTime VoucherDate { get; set; } = DateTime.Now;

        public string ReceivedFrom { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public int? InvoiceId { get; set; }

        public int? BankAccountId { get; set; }

        public string Description { get; set; } = string.Empty;
    }
}
