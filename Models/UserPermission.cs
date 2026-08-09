namespace RowadUmrahSystem.Web.Models
{
    public class UserPermission
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;

        public ApplicationUser User { get; set; } = null!;

        // Old permissions - keep temporarily
        public bool CanManageTravelers { get; set; } = false;
        public bool CanManageTrips { get; set; } = false;
        public bool CanManageBlocks { get; set; } = false;
        public bool CanViewReports { get; set; } = false;
        public bool CanManageUsers { get; set; } = false;

        // New Travelers permissions
        public bool CanViewTravelers { get; set; } = false;
        public bool CanCreateTravelers { get; set; } = false;
        public bool CanEditTravelers { get; set; } = false;
        public bool CanArchiveTravelers { get; set; } = false;
        public bool CanRestoreTravelers { get; set; } = false;

        // New Trips permissions
        public bool CanViewTrips { get; set; } = false;
        public bool CanCreateTrips { get; set; } = false;
        public bool CanArchiveTrips { get; set; } = false;
        public bool CanRestoreTrips { get; set; } = false;

        // New Documents permissions
        public bool CanViewDocuments { get; set; } = false;
        public bool CanUploadDocuments { get; set; } = false;
        public bool CanArchiveDocuments { get; set; } = false;
        public bool CanRestoreDocuments { get; set; } = false;

        // New Blocks permissions
        public bool CanViewBlocks { get; set; } = false;
        public bool CanBlockTravelers { get; set; } = false;
        public bool CanUnblockTravelers { get; set; } = false;

        // New Reports permissions
        public bool CanExportReports { get; set; } = false;

        // New Audit permission
        public bool CanViewAuditLogs { get; set; } = false;

        // =============================
        // Accounting Permissions
        // =============================

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