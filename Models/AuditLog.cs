namespace RowadUmrahSystem.Web.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        public string? UserId { get; set; }

        public string EmployeeName { get; set; } = string.Empty;

        public string Action { get; set; } = string.Empty;
        // Add, Edit, Delete, Block, Unblock

        public string EntityName { get; set; } = string.Empty;
        // Traveler, Block

        public int? TravelerId { get; set; }

        public string? PassportNumber { get; set; }

        public string? TravelerName { get; set; }

        public string? Details { get; set; }
        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}