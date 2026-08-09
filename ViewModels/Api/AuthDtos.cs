namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record LoginRequestDto(string Email, string Password, bool RememberMe);

    public sealed record AuthUserDto(
        bool IsAuthenticated,
        string? Email,
        string? FullName,
        string[] Roles);

    public sealed record LoginResponseDto(
        bool Succeeded,
        string Message,
        AuthUserDto? User);
}
