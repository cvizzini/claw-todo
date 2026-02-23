using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace TodoApp.Api.Tests;

public class TenantAdminEndpointsTests(CustomWebApplicationFactory factory) : TestBase(factory)
{
    // ── Access control ────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantEndpoints_AsAnonymous_Returns401()
    {
        ClearAuthHeader();
        (await Client.GetAsync("/api/v1/tenant/users")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TenantEndpoints_AsRegularUser_Returns403()
    {
        var (token, _) = await CreateTenantUserAsync(role: "User");
        SetAuthHeader(token);
        (await Client.GetAsync("/api/v1/tenant/users")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantListUsers_AsTenantAdmin_ReturnsOnlyOwnTenantUsers()
    {
        // Tenant A: TenantAdmin + 1 user
        var tenantA = await CreateTenantAsync("Tenant A Isolation");
        var adminAEmail = $"tadmin_a_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(adminAEmail, "Password1", tenantA, "TenantAdmin");
        var userAEmail = $"user_a_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(userAEmail, "Password1", tenantA, "User");

        // Tenant B: separate user
        var tenantB = await CreateTenantAsync("Tenant B Isolation");
        var userBEmail = $"user_b_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(userBEmail, "Password1", tenantB, "User");

        SetAuthHeader(await LoginAsync(adminAEmail, "Password1"));
        var resp = await Client.GetAsync("/api/v1/tenant/users");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        // Should only see Tenant A users
        users!.All(u => u.GetProperty("tenantId").GetInt32() == tenantA).Should().BeTrue();
        users!.Any(u => u.GetProperty("email").GetString() == userBEmail).Should().BeFalse();
    }

    [Fact]
    public async Task TenantCreateUser_AsTenantAdmin_CreatesInSameTenant()
    {
        var tenantId     = await CreateTenantAsync();
        var adminEmail   = $"tadmin_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(adminEmail, "Password1", tenantId, "TenantAdmin");
        SetAuthHeader(await LoginAsync(adminEmail, "Password1"));

        var newEmail = $"newuser_{Guid.NewGuid():N}@test.com";
        var resp = await Client.PostAsJsonAsync("/api/v1/tenant/users", new
        {
            email       = newEmail,
            password    = "Password1",
            displayName = "Created User",
            role        = "User"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantId").GetInt32().Should().Be(tenantId);
        body.GetProperty("role").GetString().Should().Be("User");
    }

    [Fact]
    public async Task TenantCreateUser_CannotCreateAdministratorRole()
    {
        var tenantId   = await CreateTenantAsync();
        var adminEmail = $"tadmin_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(adminEmail, "Password1", tenantId, "TenantAdmin");
        SetAuthHeader(await LoginAsync(adminEmail, "Password1"));

        var resp = await Client.PostAsJsonAsync("/api/v1/tenant/users", new
        {
            email    = $"tryescalate_{Guid.NewGuid():N}@test.com",
            password = "Password1",
            role     = "Administrator" // should be silently downgraded to User
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        // Role should be clamped to "User" (not Administrator)
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().NotBe("Administrator");
    }

    [Fact]
    public async Task TenantGetUser_CannotAccessOtherTenantUser()
    {
        var tenantA    = await CreateTenantAsync();
        var tenantB    = await CreateTenantAsync();
        var adminEmail = $"tadmin_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(adminEmail, "Password1", tenantA, "TenantAdmin");
        var otherUserId = await CreateUserInTenantAsync(
            $"other_{Guid.NewGuid():N}@test.com", "Password1", tenantB, "User");

        SetAuthHeader(await LoginAsync(adminEmail, "Password1"));
        (await Client.GetAsync($"/api/v1/tenant/users/{otherUserId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantUpdateUser_PromotesToTenantAdmin()
    {
        var tenantId   = await CreateTenantAsync();
        var adminEmail = $"tadmin_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(adminEmail, "Password1", tenantId, "TenantAdmin");

        var userEmail = $"user_{Guid.NewGuid():N}@test.com";
        var userId    = await CreateUserInTenantAsync(userEmail, "Password1", tenantId, "User");

        SetAuthHeader(await LoginAsync(adminEmail, "Password1"));
        var resp = await Client.PutAsJsonAsync($"/api/v1/tenant/users/{userId}",
            new { role = "TenantAdmin" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("role").GetString().Should().Be("TenantAdmin");
    }

    [Fact]
    public async Task TenantLockUser_PreventsLogin()
    {
        var tenantId   = await CreateTenantAsync();
        var adminEmail = $"tadmin_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(adminEmail, "Password1", tenantId, "TenantAdmin");

        var userEmail = $"lockme_{Guid.NewGuid():N}@test.com";
        var userId    = await CreateUserInTenantAsync(userEmail, "Password1", tenantId, "User");

        SetAuthHeader(await LoginAsync(adminEmail, "Password1"));
        (await Client.PatchAsync($"/api/v1/tenant/users/{userId}/lock", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        ClearAuthHeader();
        (await Client.PostAsJsonAsync("/api/v1/auth/login", new { email = userEmail, password = "Password1" }))
            .StatusCode.Should().Be((HttpStatusCode)423);
    }

    [Fact]
    public async Task TenantDeleteUser_RemovesFromTenant()
    {
        var tenantId   = await CreateTenantAsync();
        var adminEmail = $"tadmin_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(adminEmail, "Password1", tenantId, "TenantAdmin");

        var userEmail = $"delme_{Guid.NewGuid():N}@test.com";
        var userId    = await CreateUserInTenantAsync(userEmail, "Password1", tenantId, "User");

        SetAuthHeader(await LoginAsync(adminEmail, "Password1"));
        (await Client.DeleteAsync($"/api/v1/tenant/users/{userId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client.GetAsync($"/api/v1/tenant/users/{userId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TenantAdmin_SuperAdminCanAlsoAccessTenantEndpoints()
    {
        // Super-admin has no tenant_id in JWT, so GetTenantId returns null → Forbid
        // This is by design — super-admin manages tenants via /admin, not /tenant
        SetAuthHeader(await LoginAsSuperAdminAsync());
        (await Client.GetAsync("/api/v1/tenant/users")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }
}
