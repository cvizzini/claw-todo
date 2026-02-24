using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Infrastructure.Identity;

/// <summary>
/// Adapts ASP.NET Core Identity's UserManager into the Application layer's IAppUserAccessor.
/// Keeps Identity out of Application entirely.
/// </summary>
public class AppUserAccessor(UserManager<AppUser> userManager) : IAppUserAccessor
{
    public async Task<AppUserDto?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == email, ct);
        return user is null ? null : ToDto(user);
    }

    public async Task<AppUserDto?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        var user = await userManager.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        return user is null ? null : ToDto(user);
    }

    public Task<bool> IsLockedOutAsync(string userId)
        => userManager.Users
            .Where(u => u.Id == userId)
            .Select(u => u.LockoutEnd > DateTimeOffset.UtcNow)
            .FirstOrDefaultAsync();

    public async Task<bool> CheckPasswordAsync(string userId, string password)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is not null && await userManager.CheckPasswordAsync(user, password);
    }

    public async Task AccessFailedAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is not null) await userManager.AccessFailedAsync(user);
    }

    public async Task ResetAccessFailedCountAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is not null) await userManager.ResetAccessFailedCountAsync(user);
    }

    public async Task<IList<string>> GetRolesAsync(string userId)
    {
        var user = await userManager.FindByIdAsync(userId);
        return user is null ? [] : await userManager.GetRolesAsync(user);
    }

    private static AppUserDto ToDto(AppUser user) => new(
        user.Id,
        user.Email ?? "",
        user.DisplayName,
        user.TenantId,
        user.Tenant?.Name,
        user.Tenant?.IsActive ?? true,
        user.CreatedAt);
}
