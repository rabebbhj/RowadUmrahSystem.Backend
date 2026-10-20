namespace RowadUmrahSystem.Web.Models
{
    /// <summary>
    /// A reservation made by a signed-in account for one package departure.
    /// The total is always computed by the server from the package configuration.
    /// </summary>
    public class Booking
    {
        public int Id { get; set; }

        public string Reference { get; set; } = string.Empty;

        public string UserId { get; set; } = string.Empty;

        /// <summary>Client-generated key so a retried request never creates a second booking.</summary>
        public string? ClientRequestId { get; set; }

        public string PackageId { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public int DurationDays { get; set; }
        public string? ImageUrl { get; set; }

        public string? DepartureDate { get; set; }
        public string Nationality { get; set; } = string.Empty;
        public string? Hotel { get; set; }
        public string? RoomType { get; set; }
        public string? Transport { get; set; }
        public bool HasExistingVisa { get; set; }
        public string? VisaIssuer { get; set; }

        public int Adults { get; set; }
        public int Children { get; set; }
        public int Infants { get; set; }

        public decimal TotalAmount { get; set; }
        public string Currency { get; set; } = "د.ك";

        /// <summary>pending_payment, confirmed, cancelled.</summary>
        public string Status { get; set; } = BookingStatuses.PendingPayment;

        /// <summary>unpaid, paid. Only a verified payment provider callback may set "paid".</summary>
        public string PaymentStatus { get; set; } = BookingPaymentStatuses.Unpaid;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<BookingTraveler> Travelers { get; set; } = new List<BookingTraveler>();
    }

    public class BookingTraveler
    {
        public int Id { get; set; }

        public int BookingId { get; set; }
        public Booking Booking { get; set; } = null!;

        public int TravelerId { get; set; }
        public Traveler Traveler { get; set; } = null!;

        /// <summary>adult, child or infant.</summary>
        public string Kind { get; set; } = "adult";
    }

    public static class BookingStatuses
    {
        public const string PendingPayment = "pending_payment";
        public const string Confirmed = "confirmed";
        public const string Cancelled = "cancelled";
    }

    public static class BookingPaymentStatuses
    {
        public const string Unpaid = "unpaid";
        public const string Paid = "paid";
    }
}
