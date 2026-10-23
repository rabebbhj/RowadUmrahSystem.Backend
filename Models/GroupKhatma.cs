namespace RowadUmrahSystem.Web.Models
{
    /// <summary>
    /// Quran khatma shared by the pilgrims of one trip. A trip "group" is every non-cancelled
    /// booking of the same package on the same departure date (bookings have no explicit group).
    /// </summary>
    public class GroupKhatma
    {
        public int Id { get; set; }

        public string PackageId { get; set; } = string.Empty;
        public string DepartureDate { get; set; } = string.Empty;

        /// <summary>Optional dedication, e.g. "مهداة إلى …".</summary>
        public string? Dedication { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }

        public ICollection<GroupKhatmaHizb> Hizbs { get; set; } = new List<GroupKhatmaHizb>();
    }

    /// <summary>One of the 60 hizbs of a group khatma: free, reserved by a member, or read.</summary>
    public class GroupKhatmaHizb
    {
        public int Id { get; set; }

        public int GroupKhatmaId { get; set; }
        public GroupKhatma GroupKhatma { get; set; } = null!;

        /// <summary>1..60.</summary>
        public int Hizb { get; set; }

        /// <summary>free, reserved, read.</summary>
        public string Status { get; set; } = GroupKhatmaHizbStatuses.Free;

        public string? UserId { get; set; }
        public DateTime? ReservedAt { get; set; }
        public DateTime? ReadAt { get; set; }
    }

    public static class GroupKhatmaHizbStatuses
    {
        public const string Free = "free";
        public const string Reserved = "reserved";
        public const string Read = "read";
    }
}
