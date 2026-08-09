using RowadUmrahSystem.Web.Models.Accounting;

namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record AccountListItemDto(
        int Id,
        string Code,
        string Name,
        AccountType Type,
        int? ParentAccountId,
        string? ParentAccountName,
        bool IsActive,
        bool IsSystemAccount,
        DateTime CreatedAt);

    public sealed record AccountJournalLineDto(
        int Id,
        DateTime EntryDate,
        string EntryNumber,
        string Description,
        decimal Debit,
        decimal Credit,
        string Notes);

    public sealed record AccountDetailDto(
        int Id,
        string Code,
        string Name,
        AccountType Type,
        int? ParentAccountId,
        string? ParentAccountName,
        bool IsActive,
        bool IsSystemAccount,
        DateTime CreatedAt,
        decimal TotalDebit,
        decimal TotalCredit,
        decimal Balance,
        IReadOnlyList<AccountJournalLineDto> RecentLines);

    public sealed record AccountUpsertRequestDto(
        string Code,
        string Name,
        AccountType Type,
        int? ParentAccountId,
        bool IsActive);
}
