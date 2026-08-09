namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record BankAccountListItemDto(
        int Id,
        string BankName,
        string AccountNumber,
        string Iban,
        decimal OpeningBalance,
        bool IsCashBox,
        bool IsActive,
        DateTime CreatedAt,
        int TransactionCount);

    public sealed record BankTransactionDto(
        int Id,
        DateTime TransactionDate,
        string TransactionType,
        decimal Amount,
        string ReferenceNumber,
        string Description,
        int? JournalEntryId,
        string? JournalEntryNumber);

    public sealed record BankAccountDetailDto(
        int Id,
        string BankName,
        string AccountNumber,
        string Iban,
        decimal OpeningBalance,
        bool IsCashBox,
        bool IsActive,
        DateTime CreatedAt,
        int TransactionCount,
        IReadOnlyList<BankTransactionDto> RecentTransactions);

    public sealed record BankAccountUpsertRequestDto(
        string BankName,
        string? AccountNumber,
        string? Iban,
        decimal OpeningBalance,
        bool IsCashBox,
        bool IsActive);
}
