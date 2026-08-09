using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class PaymentVoucher
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string VoucherNumber { get; set; } = string.Empty;

        public DateTime VoucherDate { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(150)]
        public string PaidTo { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,3)")]
        public decimal Amount { get; set; }

        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

        public int? BankAccountId { get; set; }
        public BankAccount? BankAccount { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}