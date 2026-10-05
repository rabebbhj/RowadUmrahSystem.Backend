namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record TripListItemDto(
        int Id,
        int TravelerId,
        string TravelerName,
        string PassportNumber,
        string TripType,
        DateTime TripDate,
        string Status,
        string? Notes,
        DateTime CreatedAt,
        bool IsDeleted,
        DateTime? DeletedAt,
        string? DeletedBy
    );
}
