using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class ClosingEntry
    {
        public int Id { get; set; }

        public int FiscalYearId { get; set; }
        public FiscalYear FiscalYear { get; set; } = null!;

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal NetProfitOrLoss { get; set; }

        public DateTime ClosedAt { get; set; } = DateTime.Now;

        [Required]
        [MaxLength(150)]
        public string ClosedBy { get; set; } = string.Empty;
    }
}