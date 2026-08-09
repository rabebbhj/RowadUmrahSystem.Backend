using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Models;
using System.Security.Claims;

namespace RowadUmrahSystem.Web.Services
{
    public class PermissionService
    {
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

            if (await _userManager.IsInRoleAsync(user, "Admin"))
                return true;

            var permissions = await _context.UserPermissions
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == user.Id);

            if (permissions == null)
                return false;

            return permissionName switch
            {
                // Old compatibility
                "Travelers" => permissions.CanManageTravelers || permissions.CanViewTravelers,
                "Trips" => permissions.CanManageTrips || permissions.CanViewTrips,
                "Blocks" => permissions.CanManageBlocks || permissions.CanViewBlocks,
                "Reports" => permissions.CanViewReports,
                "Users" => permissions.CanManageUsers,

                // Travelers
                "Travelers.View" => permissions.CanViewTravelers || permissions.CanManageTravelers,
                "Travelers.Create" => permissions.CanCreateTravelers || permissions.CanManageTravelers,
                "Travelers.Edit" => permissions.CanEditTravelers || permissions.CanManageTravelers,
                "Travelers.Archive" => permissions.CanArchiveTravelers || permissions.CanManageTravelers,
                "Travelers.Restore" => permissions.CanRestoreTravelers || permissions.CanManageTravelers,

                // Trips
                "Trips.View" => permissions.CanViewTrips || permissions.CanManageTrips,
                "Trips.Create" => permissions.CanCreateTrips || permissions.CanManageTrips,
                "Trips.Archive" => permissions.CanArchiveTrips || permissions.CanManageTrips,
                "Trips.Restore" => permissions.CanRestoreTrips || permissions.CanManageTrips,

                // Documents
                "Documents.View" => permissions.CanViewDocuments || permissions.CanManageTravelers,
                "Documents.Upload" => permissions.CanUploadDocuments || permissions.CanManageTravelers,
                "Documents.Archive" => permissions.CanArchiveDocuments || permissions.CanManageTravelers,
                "Documents.Restore" => permissions.CanRestoreDocuments || permissions.CanManageTravelers,

                // Blocks
                "Blocks.View" => permissions.CanViewBlocks || permissions.CanManageBlocks,
                "Blocks.Block" => permissions.CanBlockTravelers || permissions.CanManageBlocks,
                "Blocks.Unblock" => permissions.CanUnblockTravelers || permissions.CanManageBlocks,

                // Reports
                "Reports.View" => permissions.CanViewReports,
                "Reports.Export" => permissions.CanExportReports || permissions.CanViewReports,

                // Audit
                "AuditLogs.View" => permissions.CanViewAuditLogs,


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
    }
}