using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;
using TodoApp.Domain.Exceptions;
using TodoApp.Domain.Interfaces;

namespace TodoApp.Application.Services;

public class AuthService(
    IAppUserAccessor users,
    IRefreshTokenRepository refreshTokens,
    ITokenService tokenService) : IAuthService
{
    public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
    {
        var user = await users.FindByEmailAsync(req.Email, ct)
            ?? throw new UnauthorizedAccessException("Invalid credentials.");

        if (await users.IsLockedOutAsync(user.Id))
            throw new AccountLockedException("Account is locked. Please contact your administrator.");

        if (!await users.CheckPasswordAsync(user.Id, req.Password))
        {
            await users.AccessFailedAsync(user.Id);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // Tenant must be active (super-admins have no tenant — always allowed)
        if (user.TenantId.HasValue && !user.TenantIsActive)
            throw new TenantInactiveException("Your organisation account is inactive. Please contact support.");

        await users.ResetAccessFailedCountAsync(user.Id);
        var roles = await users.GetRolesAsync(user.Id);
        var (token, expires) = tokenService.GenerateAccessToken(
            user.Id, user.Email, user.DisplayName, roles, user.TenantId);
        var refreshToken = await tokenService.CreateRefreshTokenAsync(user.Id, ct);

        return new AuthResponse(token, refreshToken, user.Email, user.DisplayName,
            expires, roles.FirstOrDefault() ?? "User", user.TenantId, user.TenantName);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest req, CancellationToken ct = default)
    {
        var stored = await refreshTokens.GetByTokenAsync(req.RefreshToken, ct)
            ?? throw new UnauthorizedAccessException("Invalid refresh token.");

        if (stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh token expired or revoked.");

        var user = await users.FindByIdAsync(stored.UserId, ct)
            ?? throw new UnauthorizedAccessException("User not found.");

        if (await users.IsLockedOutAsync(user.Id))
            throw new UnauthorizedAccessException("Account locked.");

        await refreshTokens.RevokeAsync(stored, ct);
        await refreshTokens.SaveChangesAsync(ct);

        var roles = await users.GetRolesAsync(user.Id);
        var (token, expires) = tokenService.GenerateAccessToken(
            user.Id, user.Email, user.DisplayName, roles, user.TenantId);
        var newRefreshToken = await tokenService.CreateRefreshTokenAsync(user.Id, ct);

        return new AuthResponse(token, newRefreshToken, user.Email, user.DisplayName,
            expires, roles.FirstOrDefault() ?? "User", user.TenantId, user.TenantName);
    }

    public async Task LogoutAsync(RefreshTokenRequest req, CancellationToken ct = default)
    {
        var stored = await refreshTokens.GetByTokenAsync(req.RefreshToken, ct);
        if (stored is not null)
        {
            await refreshTokens.RevokeAsync(stored, ct);
            await refreshTokens.SaveChangesAsync(ct);
        }
    }

    public async Task<MeResponse> GetMeAsync(string userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw new NotFoundException("User not found.");
        var roles = await users.GetRolesAsync(userId);
        return new MeResponse(user.Email, user.DisplayName,
            roles.FirstOrDefault() ?? "User", user.CreatedAt,
            user.TenantId, user.TenantName);
    }
}
