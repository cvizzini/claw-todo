using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TodoApp.Api.Data;
using TodoApp.Api.DTOs;
using TodoApp.Api.Models;

namespace TodoApp.Api.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        // POST /api/v1/auth/login
        group.MapPost("/login", async (
            LoginRequest req,
            UserManager<AppUser> userManager,
            TodoDbContext db,
            IConfiguration config) =>
        {
            var user = await userManager.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == req.Email);

            if (user is null) return Results.Unauthorized();

            if (await userManager.IsLockedOutAsync(user))
                return Results.Json(
                    new { Error = "Account is locked. Please contact your administrator." },
                    statusCode: 423);

            if (!await userManager.CheckPasswordAsync(user, req.Password))
            {
                await userManager.AccessFailedAsync(user);
                return Results.Unauthorized();
            }

            // Check tenant is active (super-admins have no tenant, always allowed)
            if (user.Tenant is not null && !user.Tenant.IsActive)
                return Results.Json(
                    new { Error = "Your organisation account is inactive. Please contact support." },
                    statusCode: 403);

            await userManager.ResetAccessFailedCountAsync(user);

            var roles = await userManager.GetRolesAsync(user);
            var (accessToken, expires) = GenerateAccessToken(user, roles, config);
            var refreshToken = await CreateRefreshToken(user.Id, db);

            return Results.Ok(BuildAuthResponse(accessToken, refreshToken, user, roles, expires));
        })
        .WithName("Login")
        .WithSummary("Login and receive a JWT + refresh token")
        .RequireRateLimiting("auth");

        // POST /api/v1/auth/refresh
        group.MapPost("/refresh", async (
            RefreshTokenRequest req,
            UserManager<AppUser> userManager,
            TodoDbContext db,
            IConfiguration config) =>
        {
            var stored = await db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == req.RefreshToken);

            if (stored is null || stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
                return Results.Unauthorized();

            var user = await userManager.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == stored.UserId);

            if (user is null || await userManager.IsLockedOutAsync(user))
                return Results.Unauthorized();

            if (user.Tenant is not null && !user.Tenant.IsActive)
                return Results.Unauthorized();

            stored.IsRevoked = true;
            var roles = await userManager.GetRolesAsync(user);
            var (accessToken, expires) = GenerateAccessToken(user, roles, config);
            var newRefreshToken = await CreateRefreshToken(user.Id, db);
            await db.SaveChangesAsync();

            return Results.Ok(BuildAuthResponse(accessToken, newRefreshToken, user, roles, expires));
        })
        .WithName("RefreshToken")
        .WithSummary("Exchange a refresh token for a new access token")
        .RequireRateLimiting("auth");

        // POST /api/v1/auth/logout
        group.MapPost("/logout", async (
            RefreshTokenRequest req,
            TodoDbContext db) =>
        {
            var stored = await db.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == req.RefreshToken);
            if (stored is not null)
            {
                stored.IsRevoked = true;
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { Message = "Logged out." });
        })
        .RequireAuthorization()
        .WithName("Logout")
        .WithSummary("Revoke refresh token and logout");

        // GET /api/v1/auth/me
        group.MapGet("/me", async (ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var user   = await userManager.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return Results.Unauthorized();

            var roles = await userManager.GetRolesAsync(user);
            return Results.Ok(new MeResponse(
                user.Email ?? "",
                user.DisplayName,
                roles.FirstOrDefault() ?? "User",
                user.CreatedAt,
                user.TenantId,
                user.Tenant?.Name
            ));
        })
        .RequireAuthorization()
        .WithName("Me")
        .WithSummary("Get the current authenticated user's profile");

        return group;
    }

    internal static (string token, DateTime expires) GenerateAccessToken(
        AppUser user, IList<string> roles, IConfiguration config)
    {
        var jwtSection = config.GetSection("Jwt");
        var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["Key"]!));
        var creds   = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTime.UtcNow.AddHours(double.Parse(jwtSection["ExpiryHours"] ?? "8"));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(ClaimTypes.Email,              user.Email!),
            new(ClaimTypes.NameIdentifier,     user.Id),
            new("display_name",                user.DisplayName ?? user.Email!),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (user.TenantId.HasValue)
            claims.Add(new Claim("tenant_id", user.TenantId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer:            jwtSection["Issuer"],
            audience:          jwtSection["Audience"],
            claims:            claims,
            expires:           expires,
            signingCredentials: creds
        );

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    internal static AuthResponse BuildAuthResponse(
        string accessToken, string refreshToken,
        AppUser user, IList<string> roles, DateTime expires) =>
        new(accessToken, refreshToken, user.Email!, user.DisplayName, expires,
            roles.FirstOrDefault() ?? "User", user.TenantId, user.Tenant?.Name);

    internal static async Task<string> CreateRefreshToken(string userId, TodoDbContext db)
    {
        var expired = await db.RefreshTokens
            .Where(t => t.UserId == userId && (t.IsRevoked || t.ExpiresAt < DateTime.UtcNow))
            .ToListAsync();
        db.RefreshTokens.RemoveRange(expired);

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        db.RefreshTokens.Add(new RefreshToken
        {
            Token     = token,
            UserId    = userId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return token;
    }
}
