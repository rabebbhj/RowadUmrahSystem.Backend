using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Models.Accounting;

namespace RowadUmrahSystem.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Traveler> Travelers { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<UserPermission> UserPermissions { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<TravelerDocument> TravelerDocuments { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<UserNotification> UserNotifications { get; set; }

        // Accounting
        public DbSet<Account> Accounts { get; set; }
        public DbSet<AccountBalance> AccountBalances { get; set; }
        public DbSet<BankAccount> BankAccounts { get; set; }
        public DbSet<BankTransaction> BankTransactions { get; set; }
        public DbSet<ClosingEntry> ClosingEntries { get; set; }
        public DbSet<CostCenter> CostCenters { get; set; }
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<ExchangeRate> ExchangeRates { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<FinancialPeriod> FinancialPeriods { get; set; }
        public DbSet<FiscalYear> FiscalYears { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<InvoiceItem> InvoiceItems { get; set; }
        public DbSet<InvoicePayment> InvoicePayments { get; set; }
        public DbSet<JournalEntry> JournalEntries { get; set; }
        public DbSet<JournalEntryLine> JournalEntryLines { get; set; }
        public DbSet<PaymentTerm> PaymentTerms { get; set; }
        public DbSet<PaymentVoucher> PaymentVouchers { get; set; }
        public DbSet<ReceiptVoucher> ReceiptVouchers { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<Tax> Taxes { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<UserPermission>()
                .HasOne(x => x.User)
                .WithOne()
                .HasForeignKey<UserPermission>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserPermission>()
                .HasIndex(x => x.UserId)
                .IsUnique();

            builder.Entity<Account>()
                .HasIndex(x => x.Code)
                .IsUnique();

            builder.Entity<Account>()
                .HasOne(x => x.ParentAccount)
                .WithMany()
                .HasForeignKey(x => x.ParentAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<JournalEntry>()
                .HasIndex(x => x.EntryNumber)
                .IsUnique();

            builder.Entity<JournalEntryLine>()
                .HasOne(x => x.JournalEntry)
                .WithMany(x => x.Lines)
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<JournalEntryLine>()
                .HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Invoice>()
                .HasIndex(x => x.InvoiceNumber)
                .IsUnique();

            builder.Entity<Invoice>()
                .HasOne(x => x.Traveler)
                .WithMany()
                .HasForeignKey(x => x.TravelerId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Invoice>()
                .HasOne(x => x.Trip)
                .WithMany()
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Invoice>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Invoice>()
    .HasOne(x => x.Currency)
    .WithMany()
    .HasForeignKey(x => x.CurrencyId)
    .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Invoice>()
                .HasOne(x => x.CostCenter)
                .WithMany()
                .HasForeignKey(x => x.CostCenterId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Invoice>()
                .HasOne(x => x.PaymentTerm)
                .WithMany()
                .HasForeignKey(x => x.PaymentTermId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<InvoiceItem>()
                .HasOne(x => x.Invoice)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<ReceiptVoucher>()
                .HasIndex(x => x.VoucherNumber)
                .IsUnique();

            builder.Entity<ReceiptVoucher>()
                .HasOne(x => x.Invoice)
                .WithMany()
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<ReceiptVoucher>()
                .HasOne(x => x.BankAccount)
                .WithMany()
                .HasForeignKey(x => x.BankAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<ReceiptVoucher>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<PaymentVoucher>()
                .HasIndex(x => x.VoucherNumber)
                .IsUnique();

            builder.Entity<PaymentVoucher>()
                .HasOne(x => x.BankAccount)
                .WithMany()
                .HasForeignKey(x => x.BankAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<PaymentVoucher>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Expense>()
                .HasOne(x => x.BankAccount)
                .WithMany()
                .HasForeignKey(x => x.BankAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Expense>()
                .HasOne(x => x.Trip)
                .WithMany()
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<Expense>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<BankAccount>()
                .HasIndex(x => x.AccountNumber);

            builder.Entity<Customer>()
                .HasOne(x => x.Traveler)
                .WithMany()
                .HasForeignKey(x => x.TravelerId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<CostCenter>()
                .HasIndex(x => x.Code)
                .IsUnique();

            builder.Entity<CostCenter>()
                .HasOne(x => x.Trip)
                .WithMany()
                .HasForeignKey(x => x.TripId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<FiscalYear>()
                .HasMany(x => x.Periods)
                .WithOne(x => x.FiscalYear)
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Currency>()
                .HasIndex(x => x.Code)
                .IsUnique();

            builder.Entity<ExchangeRate>()
                .HasOne(x => x.Currency)
                .WithMany()
                .HasForeignKey(x => x.CurrencyId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<Tax>()
                .HasIndex(x => x.Name)
                .IsUnique();

            builder.Entity<PaymentTerm>()
                .HasIndex(x => x.Name)
                .IsUnique();

            builder.Entity<ClosingEntry>()
                .HasOne(x => x.FiscalYear)
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<ClosingEntry>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<InvoicePayment>()
                .HasOne(x => x.Invoice)
                .WithMany()
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<InvoicePayment>()
                .HasOne(x => x.BankAccount)
                .WithMany()
                .HasForeignKey(x => x.BankAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<BankTransaction>()
                .HasOne(x => x.BankAccount)
                .WithMany()
                .HasForeignKey(x => x.BankAccountId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<BankTransaction>()
                .HasOne(x => x.JournalEntry)
                .WithMany()
                .HasForeignKey(x => x.JournalEntryId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<AccountBalance>()
                .HasOne(x => x.Account)
                .WithMany()
                .HasForeignKey(x => x.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<AccountBalance>()
                .HasOne(x => x.FiscalYear)
                .WithMany()
                .HasForeignKey(x => x.FiscalYearId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}