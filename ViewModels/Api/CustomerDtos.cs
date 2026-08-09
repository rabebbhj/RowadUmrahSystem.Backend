namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record CustomerListItemDto(
        int Id,
        string Name,
        string CivilId,
        string PassportNumber,
        string PhoneNumber,
        string Email,
        string Address,
        int? TravelerId,
        string? TravelerFullName,
        bool IsActive,
        DateTime CreatedAt);

    public sealed record CustomerDetailDto(
        int Id,
        string Name,
        string CivilId,
        string PassportNumber,
        string PhoneNumber,
        string Email,
        string Address,
        int? TravelerId,
        string? TravelerFullName,
        bool IsActive,
        DateTime CreatedAt);

    public sealed record CustomerUpsertRequestDto(
        string Name,
        string CivilId,
        string PassportNumber,
        string PhoneNumber,
        string Email,
        string Address,
        int? TravelerId,
        bool IsActive);
}
