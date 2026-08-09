using RowadUmrahSystem.Web.Models.Accounting;

namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record JournalEntryListItemDto(
        int Id,
        string EntryNumber,
        DateTime EntryDate,
        string Description,
        string SourceType,
        bool IsPosted,
        decimal TotalDebit,
        decimal TotalCredit,
        decimal Difference,
        int LineCount,
        DateTime CreatedAt);

    public sealed record JournalEntryLineDto(
        int Id,
        int AccountId,
        string AccountCode,
        string AccountName,
        decimal Debit,
        decimal Credit,
        string Notes);

    public sealed record JournalEntryDetailDto(
        int Id,
        string EntryNumber,
        DateTime EntryDate,
        string Description,
        string SourceType,
        int? SourceId,
        bool IsPosted,
        decimal TotalDebit,
        decimal TotalCredit,
        decimal Difference,
        DateTime CreatedAt,
        IReadOnlyList<JournalEntryLineDto> Lines);

    public sealed record JournalEntryLookupsDto(
        IReadOnlyList<LookupOptionDto> Accounts);

    public sealed class JournalEntryLineUpsertRequestDto
    {
        public int AccountId { get; set; }

        public decimal Debit { get; set; }

        public decimal Credit { get; set; }

        public string Notes { get; set; } = string.Empty;
    }

    public sealed class JournalEntryUpsertRequestDto
    {
        public DateTime EntryDate { get; set; } = DateTime.Now;

        public string Description { get; set; } = string.Empty;

        public string SourceType { get; set; } = "Manual";

        public List<JournalEntryLineUpsertRequestDto> Lines { get; set; } = new();
    }
}
