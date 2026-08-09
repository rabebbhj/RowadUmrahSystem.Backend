namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public class UserPermissionsDto
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public string UserFullName { get; set; } = string.Empty;

        public string UserEmail { get; set; } = string.Empty;

        public bool CanManageUsers { get; set; }

        public bool CanViewTravelers { get; set; }

        public bool CanCreateTravelers { get; set; }

        public bool CanEditTravelers { get; set; }

        public bool CanArchiveTravelers { get; set; }

        public bool CanRestoreTravelers { get; set; }

        public bool CanViewTrips { get; set; }

        public bool CanCreateTrips { get; set; }

        public bool CanArchiveTrips { get; set; }

        public bool CanRestoreTrips { get; set; }

        public bool CanViewDocuments { get; set; }

        public bool CanUploadDocuments { get; set; }

        public bool CanArchiveDocuments { get; set; }

        public bool CanRestoreDocuments { get; set; }

        public bool CanViewBlocks { get; set; }

        public bool CanBlockTravelers { get; set; }

        public bool CanUnblockTravelers { get; set; }

        public bool CanViewReports { get; set; }

        public bool CanExportReports { get; set; }

        public bool CanViewAuditLogs { get; set; }

        public bool CanViewAccounting { get; set; }

        public bool CanManageAccounting { get; set; }

        public bool CanManageChartOfAccounts { get; set; }

        public bool CanManageJournalEntries { get; set; }

        public bool CanManageInvoices { get; set; }

        public bool CanManageReceiptVouchers { get; set; }

        public bool CanManagePaymentVouchers { get; set; }

        public bool CanManageExpenses { get; set; }

        public bool CanManageBanks { get; set; }

        public bool CanViewFinancialReports { get; set; }
    }
}
