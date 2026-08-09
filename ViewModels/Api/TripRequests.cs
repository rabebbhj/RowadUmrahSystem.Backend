namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record TripCreateRequestDto(
        int TravelerId,
        DateTime TripDate,
        string? Notes);
}
