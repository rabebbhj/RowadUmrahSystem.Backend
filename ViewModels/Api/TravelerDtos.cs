namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record TravelerTripDto(
        int Id,
        string TripType,
        DateTime TripDate,
        string? Notes
    );

    public sealed record TravelerDocumentDto(
        int Id,
        string DocumentType,
        string FileName,
        string FilePath,
        string? Notes,
        DateTime UploadedAt
    );

    public sealed record TravelerDetailDto(
        int Id,
        string PassportNumber,
        string? PassportImagePath,
        string FullName,
        string Nationality,
        string Gender,
        DateTime DateOfBirth,
        string? Email,
        string? ResidenceNumber,
        DateTime? PassportExpiryDate,
        string PhoneNumber,
        int UmrahCount,
        bool IsBlocked,
        string? BlockReason,
        DateTime? BlockedAt,
        string? Notes,
        bool IsDeleted,
        DateTime? DeletedAt,
        string? DeletedBy,
        DateTime CreatedAt,
        IReadOnlyList<TravelerTripDto> Trips,
        IReadOnlyList<TravelerDocumentDto> Documents
    );

    public sealed class TravelerUpsertRequestDto
    {
        public string PassportNumber { get; set; } = string.Empty;
        public string? PassportImagePath { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Nationality { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime DateOfBirth { get; set; }
        public string? Email { get; set; }
        public string? ResidenceNumber { get; set; }
        public DateTime? PassportExpiryDate { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public bool IsBlocked { get; set; }
        public string? BlockReason { get; set; }
        public string? PackageId { get; set; }
        public string? PackageName { get; set; }
        public DateTime? BookingDate { get; set; }
        public string? RoomType { get; set; }
        public string? TransportType { get; set; }
        public decimal? ReservationTotal { get; set; }
    }

    public sealed class TravelerBlockRequestDto
    {
        public string BlockReason { get; set; } = string.Empty;
    }
}
