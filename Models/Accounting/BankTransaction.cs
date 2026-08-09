using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class BankTransaction
    {
        public int Id { get; set; }

        public int BankAccountId { get; set; }
        public BankAccount BankAccount { get; set; } = null!;

        public DateTime TransactionDate { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(50)]
        public string TransactionType { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,3)")]
        public decimal Amount { get; set; }

        [MaxLength(100)]
        public string ReferenceNumber { get; set; } = string.Empty;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }
    }
}