using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace TodoApp.Api.Tests;

public class AuthEndpointsTests : TestBase
{
    public AuthEndpointsTests(CustomWebApplicationFactory factory) : base(factory) { }

    // ── Login ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsToken()
    {
        var (token, tenantId) = await CreateTenantUserAsync();
        token.Should().NotBeNullOrEmpty();
        tenantId.Should().BeGreaterThan(0);

        // Also verify the raw response shape
        var email = $"loginshape_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(email, "Password1", tenantId);

        ClearAuthHeader();
        var resp = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password1" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("email").GetString().Should().Be(email);
        body.GetProperty("role").GetString().Should().Be("User");
        body.GetProperty("tenantId").GetInt32().Should().Be(tenantId);
        body.TryGetProperty("expiresAt", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var tenantId = await CreateTenantAsync();
        var email    = $"badpw_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(email, "Password1", tenantId);

        var resp = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "WrongPass9" });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentUser_ReturnsUnauthorized()
    {
        var resp = await Client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email    = "nobody@notexist.com",
            password = "Password1"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Refresh Token ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_WithValidToken_ReturnsNewTokens()
    {
        var tenantId = await CreateTenantAsync();
        var email    = $"refresh_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(email, "Password1", tenantId);

        ClearAuthHeader();
        var loginResp    = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password1" });
        var loginBody    = await loginResp.Content.ReadFromJsonAsync<JsonElement>();
        var refreshToken = loginBody.GetProperty("refreshToken").GetString()!;

        var resp = await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("token").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RefreshToken_WithInvalidToken_ReturnsUnauthorized()
    {
        (await Client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = "totally-invalid-token" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_CannotBeReused()
    {
        var tenantId = await CreateTenantAsync();
        var email    = $"reuse_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(email, "Password1", tenantId);

        ClearAuthHeader();
        var loginResp    = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password1" });
        var refreshToken = (await loginResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("refreshToken").GetString()!;

        await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken });

        (await Client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── /me ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithValidToken_ReturnsUserProfile()
    {
        var tenantId = await CreateTenantAsync();
        var email    = $"me_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(email, "Password1", tenantId);
        SetAuthHeader(await LoginAsync(email, "Password1"));

        var resp = await Client.GetAsync("/api/v1/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("email").GetString().Should().Be(email);
        body.GetProperty("tenantId").GetInt32().Should().Be(tenantId);
        ClearAuthHeader();
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        ClearAuthHeader();
        (await Client.GetAsync("/api/v1/auth/me")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
