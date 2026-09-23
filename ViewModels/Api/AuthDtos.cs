namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record LoginRequestDto(string Email, string Password, bool RememberMe);

    public sealed record AuthPermissionsDto(
        bool CanAccessDashboard,
        bool CanManageUsers,
        bool CanViewTravelers,
        bool CanCreateTravelers,
        bool CanEditTravelers,
        bool CanArchiveTravelers,
        bool CanRestoreTravelers,
        bool CanViewTrips,
        bool CanCreateTrips,
        bool CanArchiveTrips,
        bool CanRestoreTrips,
        bool CanViewDocuments,
        bool CanUploadDocuments,
        bool CanArchiveDocuments,
        bool CanRestoreDocuments,
        bool CanViewBlocks,
        bool CanBlockTravelers,
        bool CanUnblockTravelers,
        bool CanViewReports,
        bool CanExportReports,
        bool CanViewAuditLogs,
        bool CanViewAccounting,
        bool CanManageAccounting,
        bool CanManageChartOfAccounts,
        bool CanManageJournalEntries,
        bool CanManageInvoices,
        bool CanManageReceiptVouchers,
        bool CanManagePaymentVouchers,
        bool CanManageExpenses,
        bool CanManageBanks,
        bool CanViewFinancialReports);

    public sealed record AuthUserDto(
        bool IsAuthenticated,
        string? Email,
        string? FullName,
        string[] Roles,
        AuthPermissionsDto? Permissions = null);

    public sealed record LoginResponseDto(
        bool Succeeded,
        string Message,
        AuthUserDto? User);
}
