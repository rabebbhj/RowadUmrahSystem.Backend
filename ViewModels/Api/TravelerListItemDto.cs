namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record TravelerListItemDto(
        int Id,
        string PassportNumber,
        string FullName,
        string Nationality,
        string Gender,
        DateTime DateOfBirth,
        string? Email,
        string PhoneNumber,
        int UmrahCount,
        bool IsBlocked,
        bool IsDeleted,
        string? BlockReason,
        DateTime? BlockedAt,
        string? Notes,
        string? PassportImagePath,
        DateTime? PassportExpiryDate,
        DateTime CreatedAt,
        int TripCount,
        DateTime? DeletedAt,
        string? DeletedBy
    );
}
