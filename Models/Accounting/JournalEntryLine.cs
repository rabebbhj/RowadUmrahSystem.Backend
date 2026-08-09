using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class JournalEntryLine
    {
        public int Id { get; set; }

        public int JournalEntryId { get; set; }
        public JournalEntry JournalEntry { get; set; } = null!;

        public int AccountId { get; set; }
        public Account Account { get; set; } = null!;

        [Column(TypeName = "decimal(18,3)")]
        public decimal Debit { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal Credit { get; set; }

        [MaxLength(300)]
        public string Notes { get; set; } = string.Empty;
    }
}