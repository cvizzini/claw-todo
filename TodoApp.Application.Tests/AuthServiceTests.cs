using NSubstitute;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;
using TodoApp.Application.Services;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Exceptions;
using TodoApp.Domain.Interfaces;
using FluentAssertions;

namespace TodoApp.Application.Tests;

/// <summary>
/// Pure unit tests for AuthService — no DB, no HTTP, no infrastructure.
/// </summary>
public class AuthServiceTests
{
    private readonly IAppUserAccessor        _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenService           _tokenService;
    private readonly AuthService             _sut;

    public AuthServiceTests()
    {
        _users         = Substitute.For<IAppUserAccessor>();
        _refreshTokens = Substitute.For<IRefreshTokenRepository>();
        _tokenService  = Substitute.For<ITokenService>();
        _sut           = new AuthService(_users, _refreshTokens, _tokenService);
    }

    // ── LoginAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsAuthResponse()
    {
        var user = MakeUser();
        _users.FindByEmailAsync("test@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _users.IsLockedOutAsync(user.Id).Returns(false);
        _users.CheckPasswordAsync(user.Id, "pass123").Returns(true);
        _users.GetRolesAsync(user.Id).Returns(new List<string> { "User" });
        _tokenService.GenerateAccessToken(user.Id, user.Email, user.DisplayName, Arg.Any<IList<string>>(), user.TenantId)
                     .Returns(("jwt-token", DateTime.UtcNow.AddHours(8)));
        _tokenService.CreateRefreshTokenAsync(user.Id, Arg.Any<CancellationToken>())
                     .Returns("refresh-token");

        var result = await _sut.LoginAsync(new LoginRequest("test@example.com", "pass123"));

        result.Token.Should().Be("jwt-token");
        result.RefreshToken.Should().Be("refresh-token");
        result.Email.Should().Be("test@example.com");
        result.Role.Should().Be("User");
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsUnauthorized()
    {
        _users.FindByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUserDto?)null);

        await _sut.Invoking(s => s.LoginAsync(new LoginRequest("nobody@example.com", "pw")))
                  .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task LoginAsync_LockedOut_ThrowsAccountLockedException()
    {
        var user = MakeUser();
        _users.FindByEmailAsync("test@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _users.IsLockedOutAsync(user.Id).Returns(true);

        await _sut.Invoking(s => s.LoginAsync(new LoginRequest("test@example.com", "pass123")))
                  .Should().ThrowAsync<AccountLockedException>();
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_ThrowsUnauthorizedAndIncrementsFailCount()
    {
        var user = MakeUser();
        _users.FindByEmailAsync("test@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _users.IsLockedOutAsync(user.Id).Returns(false);
        _users.CheckPasswordAsync(user.Id, "wrong").Returns(false);

        await _sut.Invoking(s => s.LoginAsync(new LoginRequest("test@example.com", "wrong")))
                  .Should().ThrowAsync<UnauthorizedAccessException>();

        await _users.Received(1).AccessFailedAsync(user.Id);
    }

    [Fact]
    public async Task LoginAsync_InactiveTenant_ThrowsTenantInactiveException()
    {
        var user = MakeUser(tenantId: 5, tenantIsActive: false);
        _users.FindByEmailAsync("test@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _users.IsLockedOutAsync(user.Id).Returns(false);
        _users.CheckPasswordAsync(user.Id, "pass123").Returns(true);

        await _sut.Invoking(s => s.LoginAsync(new LoginRequest("test@example.com", "pass123")))
                  .Should().ThrowAsync<TenantInactiveException>();
    }

    [Fact]
    public async Task LoginAsync_SuperAdmin_NoTenant_Succeeds()
    {
        // Super-admin: TenantId = null, always allowed
        var user = MakeUser(tenantId: null);
        _users.FindByEmailAsync("admin@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _users.IsLockedOutAsync(user.Id).Returns(false);
        _users.CheckPasswordAsync(user.Id, "admin-pass").Returns(true);
        _users.GetRolesAsync(user.Id).Returns(new List<string> { "Administrator" });
        _tokenService.GenerateAccessToken(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                          Arg.Any<IList<string>>(), null)
                     .Returns(("admin-token", DateTime.UtcNow.AddHours(8)));
        _tokenService.CreateRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                     .Returns("admin-refresh");

        var result = await _sut.LoginAsync(new LoginRequest("admin@example.com", "admin-pass"));

        result.Role.Should().Be("Administrator");
        result.TenantId.Should().BeNull();
    }

    // ── RefreshAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshAsync_ValidToken_ReturnsNewTokenPair()
    {
        var user          = MakeUser();
        var storedToken   = new RefreshToken
        {
            Token     = "old-refresh",
            UserId    = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            IsRevoked = false
        };

        _refreshTokens.GetByTokenAsync("old-refresh", Arg.Any<CancellationToken>()).Returns(storedToken);
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _users.IsLockedOutAsync(user.Id).Returns(false);
        _users.GetRolesAsync(user.Id).Returns(new List<string> { "User" });
        _tokenService.GenerateAccessToken(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
                                          Arg.Any<IList<string>>(), Arg.Any<int?>())
                     .Returns(("new-jwt", DateTime.UtcNow.AddHours(8)));
        _tokenService.CreateRefreshTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                     .Returns("new-refresh");
        _refreshTokens.RevokeAsync(storedToken, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _refreshTokens.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var result = await _sut.RefreshAsync(new RefreshTokenRequest("old-refresh"));

        result.Token.Should().Be("new-jwt");
        result.RefreshToken.Should().Be("new-refresh");
        await _refreshTokens.Received(1).RevokeAsync(storedToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAsync_InvalidToken_ThrowsUnauthorized()
    {
        _refreshTokens.GetByTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .Returns((RefreshToken?)null);

        await _sut.Invoking(s => s.RefreshAsync(new RefreshTokenRequest("bad-token")))
                  .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_RevokedToken_ThrowsUnauthorized()
    {
        var storedToken = new RefreshToken
        {
            Token     = "revoked",
            UserId    = "user-1",
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            IsRevoked = true
        };
        _refreshTokens.GetByTokenAsync("revoked", Arg.Any<CancellationToken>()).Returns(storedToken);

        await _sut.Invoking(s => s.RefreshAsync(new RefreshTokenRequest("revoked")))
                  .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ThrowsUnauthorized()
    {
        var storedToken = new RefreshToken
        {
            Token     = "expired",
            UserId    = "user-1",
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            IsRevoked = false
        };
        _refreshTokens.GetByTokenAsync("expired", Arg.Any<CancellationToken>()).Returns(storedToken);

        await _sut.Invoking(s => s.RefreshAsync(new RefreshTokenRequest("expired")))
                  .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    // ── LogoutAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LogoutAsync_ValidToken_RevokesIt()
    {
        var storedToken = new RefreshToken
        {
            Token     = "valid-refresh",
            UserId    = "user-1",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            IsRevoked = false
        };
        _refreshTokens.GetByTokenAsync("valid-refresh", Arg.Any<CancellationToken>()).Returns(storedToken);
        _refreshTokens.RevokeAsync(storedToken, Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _refreshTokens.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        await _sut.LogoutAsync(new RefreshTokenRequest("valid-refresh"));

        await _refreshTokens.Received(1).RevokeAsync(storedToken, Arg.Any<CancellationToken>());
        await _refreshTokens.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LogoutAsync_UnknownToken_DoesNotThrow()
    {
        _refreshTokens.GetByTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .Returns((RefreshToken?)null);

        // Should silently do nothing
        await _sut.Invoking(s => s.LogoutAsync(new RefreshTokenRequest("unknown")))
                  .Should().NotThrowAsync();
    }

    // ── GetMeAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMeAsync_KnownUser_ReturnsMeResponse()
    {
        var user = MakeUser();
        _users.FindByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _users.GetRolesAsync(user.Id).Returns(new List<string> { "User" });

        var result = await _sut.GetMeAsync(user.Id);

        result.Email.Should().Be(user.Email);
        result.DisplayName.Should().Be(user.DisplayName);
        result.Role.Should().Be("User");
    }

    [Fact]
    public async Task GetMeAsync_UnknownUser_ThrowsNotFoundException()
    {
        _users.FindByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((AppUserDto?)null);

        await _sut.Invoking(s => s.GetMeAsync("missing-id"))
                  .Should().ThrowAsync<NotFoundException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AppUserDto MakeUser(int? tenantId = 1, bool tenantIsActive = true) =>
        new("user-abc", "test@example.com", "Test User",
            tenantId, tenantId.HasValue ? "Test Tenant" : null,
            tenantIsActive, DateTime.UtcNow.AddDays(-10));
}
