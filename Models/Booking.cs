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

        /// <summary>Men and women among the adults (booking flow v2; 0 for older bookings).</summary>
        public int Men { get; set; }
        public int Women { get; set; }

        /// <summary>Family: private rooms; otherwise beds in single-sex shared rooms.</summary>
        public bool FamilyMode { get; set; }

        /// <summary>Hotel and rooms per city, as sent by the app (JSON of DraftStay[]).</summary>
        public string? StaysJson { get; set; }

        /// <summary>Price lines computed by the server (JSON of QuoteLine[]).</summary>
        public string? PriceBreakdownJson { get; set; }

        /// <summary>Seats and beds are held until this time while the payment is pending.</summary>
        public DateTime? HoldExpiresAt { get; set; }

        public string? PaymentInvoiceId { get; set; }
        public string? PaymentId { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentReturnUrl { get; set; }
        public string? LastPaymentError { get; set; }
        public DateTime? PaidAt { get; set; }

        /// <summary>Set when a paid booking could not get all its beds: the back-office places it by hand.</summary>
        public bool NeedsRoomReview { get; set; }

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

        /// <summary>rowad (Rowad issues the visa) or existing (the traveler already has one).</summary>
        public string? VisaOption { get; set; }

        /// <summary>male or female, from the passport; places the traveler in a shared room.</summary>
        public string? Gender { get; set; }

        /// <summary>Nationality read from the passport; groups shared rooms by nationality.</summary>
        public string? Nationality { get; set; }
    }

    public static class BookingStatuses
    {
        public const string PendingPayment = "pending_payment";
        public const string Confirmed = "confirmed";
        public const string Cancelled = "cancelled";
        public const string Expired = "expired";
    }

    public static class BookingPaymentStatuses
    {
        public const string Unpaid = "unpaid";
        public const string Paid = "paid";
    }
}
