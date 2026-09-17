using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Services;

namespace RowadUmrahSystem.Web.Controllers
{
    [Authorize]
    public class AccountingController : Controller
    {  
        private readonly ApplicationDbContext _context;
        private readonly PermissionService _permissionService;

        public AccountingController(
            ApplicationDbContext context,
            PermissionService permissionService)
        {
            _context = context;
            _permissionService = permissionService;
        }

        private async Task<bool> HasPermission(string permission)
        {
            return await _permissionService.HasPermissionAsync(User, permission);
        }

        public async Task<IActionResult> Index()
        {
            if (!await HasPermission("Accounting.View"))
                return Forbid();

            var invoiceAmounts = await _context.Invoices
                .AsNoTracking()
                .Select(x => x.TotalAmount)
                .ToListAsync();

            var receiptAmounts = await _context.ReceiptVouchers
                .AsNoTracking()
                .Select(x => x.Amount)
                .ToListAsync();

            var paymentAmounts = await _context.PaymentVouchers
                .AsNoTracking()
                .Select(x => x.Amount)
                .ToListAsync();

            var expenseAmounts = await _context.Expenses
                .AsNoTracking()
                .Select(x => x.Amount)
                .ToListAsync();

            var bankOpeningAmounts = await _context.BankAccounts
                .AsNoTracking()
                .Select(x => x.OpeningBalance)
                .ToListAsync();

            decimal totalInvoices = invoiceAmounts.Sum();
            decimal totalReceipts = receiptAmounts.Sum();   
            decimal totalPayments = paymentAmounts.Sum();
            decimal totalExpenses = expenseAmounts.Sum();
            decimal bankOpeningBalances = bankOpeningAmounts.Sum();

            ViewBag.TotalInvoices = totalInvoices;
            ViewBag.TotalReceipts = totalReceipts;
            ViewBag.TotalPayments = totalPayments;
            ViewBag.TotalExpenses = totalExpenses;
            ViewBag.BankOpeningBalances = bankOpeningBalances;
            ViewBag.NetBalance = totalReceipts + bankOpeningBalances - totalPayments - totalExpenses;
            ViewBag.OutstandingInvoices = totalInvoices - totalReceipts;

            ViewBag.CanManageAccounting = await HasPermission("Accounting.Manage");
            ViewBag.CanManageChartOfAccounts = await HasPermission("Accounting.ChartOfAccounts");
            ViewBag.CanManageJournalEntries = await HasPermission("Accounting.JournalEntries");
            ViewBag.CanManageInvoices = await HasPermission("Accounting.Invoices");
            ViewBag.CanManageReceiptVouchers = await HasPermission("Accounting.ReceiptVouchers");
            ViewBag.CanManagePaymentVouchers = await HasPermission("Accounting.PaymentVouchers");
            ViewBag.CanManageExpenses = await HasPermission("Accounting.Expenses");
            ViewBag.CanManageBanks = await HasPermission("Accounting.Banks");
            ViewBag.CanViewFinancialReports = await HasPermission("Accounting.Reports");

            return View();
        }

        public async Task<IActionResult> ChartOfAccounts()
        {
            if (!await HasPermission("Accounting.ChartOfAccounts"))
                return Forbid();

            return RedirectToAction("Index", "Accounts");
        }

        public async Task<IActionResult> JournalEntries()
        {
            if (!await HasPermission("Accounting.JournalEntries"))
                return Forbid();

            return RedirectToAction("Index", "JournalEntries");
        }

        public async Task<IActionResult> Invoices()
        {
            if (!await HasPermission("Accounting.Invoices"))
                return Forbid();

            return RedirectToAction("Index", "Invoices");
        }

        public async Task<IActionResult> ReceiptVouchers()
        {
            if (!await HasPermission("Accounting.ReceiptVouchers"))
                return Forbid();

            return RedirectToAction("Index", "ReceiptVouchers");
        }

        public async Task<IActionResult> PaymentVouchers()
        {
            if (!await HasPermission("Accounting.PaymentVouchers"))
                return Forbid();

            return RedirectToAction("Index", "PaymentVouchers");
        }

        public async Task<IActionResult> Expenses()
        {
            if (!await HasPermission("Accounting.Expenses"))
                return Forbid();

            return RedirectToAction("Index", "Expenses");
        }

        public async Task<IActionResult> Banks()
        {
            if (!await HasPermission("Accounting.Banks"))
                return Forbid();

            return RedirectToAction("Index", "BankAccounts");
        }

        public async Task<IActionResult> Reports()
        {
            if (!await HasPermission("Accounting.Reports"))
                return Forbid();

            return RedirectToAction("Index", "FinancialReports");
        }
    }
}