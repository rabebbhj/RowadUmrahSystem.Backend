using System.ComponentModel.DataAnnotations;

namespace RowadUmrahSystem.Web.Models
{
    public class TravelerDocument
    {
        public int Id { get; set; }

        [Required]
        public int TravelerId { get; set; }

        public Traveler? Traveler { get; set; }

        [Required]
        public string DocumentType { get; set; } = string.Empty;
        // Passport, PersonalPhoto, Visa, PDF, Other

        [Required]
        public string FileName { get; set; } = string.Empty;

        [Required]
        public string FilePath { get; set; } = string.Empty;

        public string? Notes { get; set; }
        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        public string? DeletedBy { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.Now;
    }
}