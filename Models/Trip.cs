namespace RowadUmrahSystem.Web.Models
{
    public class Trip
    {
        public int Id { get; set; }

        public int TravelerId { get; set; }

        public Traveler Traveler { get; set; } = null!;

        public string TripType { get; set; } = "Umrah";

        public DateTime TripDate { get; set; }

        public string? Notes { get; set; }

        public string Status { get; set; } = "pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        public string? DeletedBy { get; set; }
    }
}
