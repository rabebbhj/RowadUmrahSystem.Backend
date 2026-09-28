namespace RowadUmrahSystem.Web.Models
{
    public class Traveler
    {
        public int Id { get; set; }

        public string PassportNumber { get; set; } = string.Empty;
        public string? PassportImagePath { get; set; }

        public string FullName { get; set; } = string.Empty;
        public string? FirstNameArabic { get; set; }
        public string? FatherNameArabic { get; set; }
        public string? GrandFatherNameArabic { get; set; }
        public string? FamilyNameArabic { get; set; }
        public string? FirstNameEnglish { get; set; }
        public string? FatherNameEnglish { get; set; }
        public string? GrandFatherNameEnglish { get; set; }
        public string? FamilyNameEnglish { get; set; }

        public string Nationality { get; set; } = string.Empty;

        public string Gender { get; set; } = string.Empty;
        public string? Profession { get; set; }
        public string? BirthCountry { get; set; }
        public string? BirthCity { get; set; }
        public string? MaritalStatus { get; set; }

        public ICollection<Trip> Trips { get; set; } = new List<Trip>();

        public DateTime DateOfBirth { get; set; }
        public string? Email { get; set; }

        public string? ResidenceNumber { get; set; }
        public DateTime? ResidenceExpiryDate { get; set; }

        public DateTime? PassportExpiryDate { get; set; }

        public string PhoneNumber { get; set; } = string.Empty;

        public int UmrahCount { get; set; } = 0;

        public bool IsBlocked { get; set; } = false;
        public string? BlockReason { get; set; }

        public DateTime? BlockedAt { get; set; }

        public string? Notes { get; set; }
        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        public string? DeletedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool DocumentsReviewed { get; set; } = false;
        public DateTime? DocumentsReviewedAt { get; set; }
        public ICollection<TravelerDocument> Documents { get; set; } = new List<TravelerDocument>();
    }
}
