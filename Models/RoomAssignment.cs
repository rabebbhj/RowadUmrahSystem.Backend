namespace RowadUmrahSystem.Web.Models
{
    /// <summary>
    /// A bed held by a traveler in one room of a hotel for one departure. A room is identified by
    /// (PackageId, DepartureDate, City, HotelId, RoomType, RoomNumber). Family rooms are private;
    /// shared rooms take one sex and are rebalanced by nationality until the rooming list freezes.
    /// </summary>
    public class RoomAssignment
    {
        public int Id { get; set; }

        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public int TravelerId { get; set; }

        public string PackageId { get; set; } = string.Empty;
        public string DepartureDate { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string HotelId { get; set; } = string.Empty;
        public string RoomType { get; set; } = string.Empty;
        public int RoomNumber { get; set; }

        /// <summary>male or female.</summary>
        public string Gender { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public bool IsFamily { get; set; }

        /// <summary>Locked by the back-office: never moved by the automatic rebalancing.</summary>
        public bool IsLocked { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>Audit trail of every bed move: who, from which room to which, when and why.</summary>
    public class RoomMoveLog
    {
        public int Id { get; set; }
        public int BookingId { get; set; }
        public int TravelerId { get; set; }
        public string PackageId { get; set; } = string.Empty;
        public string DepartureDate { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string HotelId { get; set; } = string.Empty;
        public string RoomType { get; set; } = string.Empty;
        public int FromRoom { get; set; }
        public int ToRoom { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string MovedBy { get; set; } = "system";
        public DateTime MovedAt { get; set; } = DateTime.UtcNow;
    }
}
