using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.Models.Accounting
{
    public class JournalEntry
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string EntryNumber { get; set; } = string.Empty;

        public DateTime EntryDate { get; set; } = DateTime.Now;

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(50)]
        public string SourceType { get; set; } = "Manual";

        public int? SourceId { get; set; }

        public bool IsPosted { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public List<JournalEntryLine> Lines { get; set; } = new();
    }
}
