using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using System.Security.Claims;

namespace RowadUmrahSystem.Web.Services
{
    public class PermissionService
    {
        private const string MainAdminEmail = "admin@rowad.local";
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public PermissionService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<bool> HasPermissionAsync(
            ClaimsPrincipal userPrincipal,
            string permissionName)
        {
            var user = await _userManager.GetUserAsync(userPrincipal);

            if (user == null)
                return false;

            if (!user.IsActive)
                return false;

            if (IsMainAdmin(user))
                return true;

            var permissions = await _context.UserPermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (permissions == null)
                return false;

            return permissionName switch
            {
                // Old compatibility
                "Travelers" => permissions.CanViewTravelers,
                "Trips" => permissions.CanViewTrips,
                "Blocks" => permissions.CanViewBlocks,
                "Reports" => permissions.CanViewReports,
                "Users" => permissions.CanManageUsers,
                "Dashboard" => permissions.CanAccessDashboard,
                "Notifications" => permissions.CanViewNotifications,

                // Travelers
                "Travelers.View" => permissions.CanViewTravelers,
                "Travelers.Create" => permissions.CanCreateTravelers,
                "Travelers.Edit" => permissions.CanEditTravelers,
                "Travelers.Archive" => permissions.CanArchiveTravelers,
                "Travelers.Restore" => permissions.CanRestoreTravelers,

                // Trips
                "Trips.View" => permissions.CanViewTrips,
                "Trips.Create" => permissions.CanCreateTrips,
                "Trips.Archive" => permissions.CanArchiveTrips,
                "Trips.Restore" => permissions.CanRestoreTrips,

                // Documents
                "Documents.View" => permissions.CanViewDocuments,
                "Documents.Upload" => permissions.CanUploadDocuments,
                "Documents.Archive" => permissions.CanArchiveDocuments,
                "Documents.Restore" => permissions.CanRestoreDocuments,

                // Blocks
                "Blocks.View" => permissions.CanViewBlocks,
                "Blocks.Block" => permissions.CanBlockTravelers,
                "Blocks.Unblock" => permissions.CanUnblockTravelers,

                // Reports
                "Reports.View" => permissions.CanViewReports,
                "Reports.Export" => permissions.CanExportReports,

                // Audit
                "AuditLogs.View" => permissions.CanViewAuditLogs,

                // Dashboard and notifications
                "Dashboard.View" => permissions.CanAccessDashboard,
                "Notifications.View" => permissions.CanViewNotifications,

                // Accounting
                "Accounting.View" =>
                    permissions.CanViewAccounting ||
                    permissions.CanManageAccounting,

                "Accounting.Manage" =>
                    permissions.CanManageAccounting,

                "Accounting.ChartOfAccounts" =>
                    permissions.CanManageChartOfAccounts ||
                    permissions.CanManageAccounting,

                "Accounting.JournalEntries" =>
                    permissions.CanManageJournalEntries ||
                    permissions.CanManageAccounting,

                "Accounting.Invoices" =>
                    permissions.CanManageInvoices ||
                    permissions.CanManageAccounting,

                "Accounting.ReceiptVouchers" =>
                    permissions.CanManageReceiptVouchers ||
                    permissions.CanManageAccounting,

                "Accounting.PaymentVouchers" =>
                    permissions.CanManagePaymentVouchers ||
                    permissions.CanManageAccounting,

                "Accounting.Expenses" =>
                    permissions.CanManageExpenses ||
                    permissions.CanManageAccounting,

                "Accounting.Banks" =>
                    permissions.CanManageBanks ||
                    permissions.CanManageAccounting,

                "Accounting.Reports" =>
                    permissions.CanViewFinancialReports ||
                    permissions.CanManageAccounting,
                // Users
                "Users.Manage" => permissions.CanManageUsers,

                _ => false
            };
        }

        private static bool IsMainAdmin(ApplicationUser user)
        {
            return string.Equals(user.Email, MainAdminEmail, StringComparison.OrdinalIgnoreCase);
        }
    }
}
