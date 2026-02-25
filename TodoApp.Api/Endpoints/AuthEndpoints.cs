using System.Security.Claims;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;

namespace TodoApp.Api.Endpoints;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest req, IAuthService authService) =>
        {
            var result = await authService.LoginAsync(req);
            return Results.Ok(result);
        })
        .WithName("Login")
        .WithSummary("Login and receive a JWT + refresh token")
        .RequireRateLimiting("auth");

        group.MapPost("/refresh", async (RefreshTokenRequest req, IAuthService authService) =>
        {
            var result = await authService.RefreshAsync(req);
            return Results.Ok(result);
        })
        .WithName("RefreshToken")
        .WithSummary("Exchange a refresh token for a new access token")
        .RequireRateLimiting("auth");

        group.MapPost("/logout", async (RefreshTokenRequest req, IAuthService authService) =>
        {
            await authService.LogoutAsync(req);
            return Results.Ok(new { Message = "Logged out." });
        })
        .RequireAuthorization()
        .WithName("Logout")
        .WithSummary("Revoke refresh token and logout");

        group.MapGet("/me", async (ClaimsPrincipal principal, IAuthService authService) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result = await authService.GetMeAsync(userId);
            return Results.Ok(result);
        })
        .RequireAuthorization()
        .WithName("Me")
        .WithSummary("Get the current authenticated user's profile");

        return app;
    }
}
