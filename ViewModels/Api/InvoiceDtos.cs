using RowadUmrahSystem.Web.Models.Accounting;

namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record LookupOptionDto(
        int Id,
        string Label);

    public sealed record InvoiceListItemDto(
        int Id,
        string InvoiceNumber,
        DateTime InvoiceDate,
        DateTime? DueDate,
        string CustomerName,
        string? PassportNumber,
        int? TravelerId,
        string? TravelerName,
        int? TripId,
        string? TripLabel,
        int? CurrencyId,
        string? CurrencyCode,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal RemainingAmount,
        InvoiceStatus Status,
        DateTime CreatedAt);

    public sealed record InvoiceItemDto(
        int Id,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal TaxRate,
        decimal LineTotal,
        int SortOrder);

    public sealed record InvoiceJournalEntryLineDto(
        int Id,
        int AccountId,
        string AccountCode,
        string AccountName,
        decimal Debit,
        decimal Credit,
        string Notes);

    public sealed record InvoiceDetailDto(
        int Id,
        string InvoiceNumber,
        DateTime InvoiceDate,
        DateTime? DueDate,
        string CustomerName,
        string? PassportNumber,
        int? TravelerId,
        string? TravelerName,
        int? TripId,
        string? TripLabel,
        int? CurrencyId,
        string? CurrencyCode,
        int? CostCenterId,
        string? CostCenterCode,
        int? PaymentTermId,
        string? PaymentTermName,
        PaymentMethod PaymentMethod,
        string ReferenceNumber,
        string Notes,
        decimal SubTotal,
        decimal DiscountAmount,
        decimal TaxAmount,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal RemainingAmount,
        InvoiceStatus Status,
        bool IsArchived,
        DateTime? PaidAt,
        int? JournalEntryId,
        string? JournalEntryNumber,
        DateTime? JournalEntryDate,
        string? JournalEntryDescription,
        bool JournalEntryIsPosted,
        DateTime CreatedAt,
        IReadOnlyList<InvoiceItemDto> Items,
        IReadOnlyList<InvoiceJournalEntryLineDto> JournalEntryLines);

    public sealed record InvoiceLookupsDto(
        IReadOnlyList<LookupOptionDto> Travelers,
        IReadOnlyList<LookupOptionDto> Trips,
        IReadOnlyList<LookupOptionDto> Currencies,
        IReadOnlyList<LookupOptionDto> CostCenters,
        IReadOnlyList<LookupOptionDto> PaymentTerms,
        IReadOnlyList<LookupOptionDto> ReceivableAccounts,
        IReadOnlyList<LookupOptionDto> RevenueAccounts);

    public sealed class InvoiceItemUpsertRequestDto
    {
        public string Description { get; set; } = string.Empty;

        public decimal Quantity { get; set; } = 1;

        public decimal UnitPrice { get; set; }

        public decimal DiscountAmount { get; set; }

        public decimal TaxRate { get; set; }
    }

    public sealed class InvoiceUpsertRequestDto
    {
        public int? TravelerId { get; set; }

        public int? TripId { get; set; }

        public int? CurrencyId { get; set; }

        public int? CostCenterId { get; set; }

        public int? PaymentTermId { get; set; }

        public string CustomerName { get; set; } = string.Empty;

        public string? PassportNumber { get; set; }

        public DateTime InvoiceDate { get; set; } = DateTime.Now;

        public DateTime? DueDate { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public string ReferenceNumber { get; set; } = string.Empty;

        public int ReceivableAccountId { get; set; }

        public int RevenueAccountId { get; set; }

        public string Notes { get; set; } = string.Empty;

        public List<InvoiceItemUpsertRequestDto> Items { get; set; } = new();
    }
}
