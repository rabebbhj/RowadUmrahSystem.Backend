namespace RowadUmrahSystem.Web.ViewModels.Api
{
    public sealed record UserListItemDto(
        string Id,
        string FullName,
        string UserName,
        string Email,
        string? PhoneNumber,
        bool IsActive,
        DateTime CreatedAt,
        DateTime? LastLoginAt,
        bool IsMainAdmin,
        string[] Roles,
        bool HasPermissions);
}
