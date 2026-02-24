using TodoApp.Application.DTOs;

namespace TodoApp.Application.Interfaces;

/// <summary>
/// Abstraction over ASP.NET Core Identity's UserManager.
/// Keeps Application layer free of Identity dependencies.
/// </summary>
public interface IAppUserAccessor
{
    Task<AppUserDto?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<AppUserDto?> FindByIdAsync(string id, CancellationToken ct = default);
    Task<bool> IsLockedOutAsync(string userId);
    Task<bool> CheckPasswordAsync(string userId, string password);
    Task AccessFailedAsync(string userId);
    Task ResetAccessFailedCountAsync(string userId);
    Task<IList<string>> GetRolesAsync(string userId);
}

public record AppUserDto(
    string Id, string Email, string? DisplayName,
    int? TenantId, string? TenantName,
    bool TenantIsActive, DateTime CreatedAt);
