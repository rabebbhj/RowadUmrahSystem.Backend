namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record UserCreateRequestDto(
        string FullName,
        string Email,
        string? PhoneNumber,
        string Password);
}
