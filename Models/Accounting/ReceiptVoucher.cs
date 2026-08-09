using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class ReceiptVoucher
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string VoucherNumber { get; set; } = string.Empty;

        public DateTime VoucherDate { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(150)]
        public string ReceivedFrom { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,3)")]
        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public int? InvoiceId { get; set; }
        public Invoice? Invoice { get; set; }

        public int? BankAccountId { get; set; }
        public BankAccount? BankAccount { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}