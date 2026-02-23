using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace TodoApp.Api.Tests;

public class AdminEndpointsTests(CustomWebApplicationFactory factory) : TestBase(factory)
{
    // ── Access control ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListUsers_AsAnonymous_Returns401()
    {
        ClearAuthHeader();
        (await Client.GetAsync("/api/v1/admin/users")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListUsers_AsRegularUser_Returns403()
    {
        var (token, _) = await CreateTenantUserAsync();
        SetAuthHeader(token);
        (await Client.GetAsync("/api/v1/admin/users")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListUsers_AsTenantAdmin_Returns403()
    {
        var (token, _) = await CreateTenantUserAsync(role: "TenantAdmin");
        SetAuthHeader(token);
        (await Client.GetAsync("/api/v1/admin/users")).StatusCode
            .Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListUsers_AsSuperAdmin_ReturnsAllUsers()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var resp = await Client.GetAsync("/api/v1/admin/users");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        body.Should().NotBeNullOrEmpty();
    }

    // ── Tenant CRUD ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateTenant_AsSuperAdmin_Succeeds()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var slug = $"t-{Guid.NewGuid():N}".Substring(0, 20);
        var resp = await Client.PostAsJsonAsync("/api/v1/admin/tenants", new
        {
            name = "Test Corp",
            slug
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("slug").GetString().Should().Be(slug);
        body.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task CreateTenant_DuplicateSlug_Returns409()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var slug = $"slug-{Guid.NewGuid():N}".Substring(0, 20);
        await Client.PostAsJsonAsync("/api/v1/admin/tenants", new { name = "First", slug });
        var resp = await Client.PostAsJsonAsync("/api/v1/admin/tenants", new { name = "Second", slug });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ListTenants_AsSuperAdmin_ReturnsTenants()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        // Ensure at least one exists
        await CreateTenantAsync("List Tenants Test");
        var resp = await Client.GetAsync("/api/v1/admin/tenants");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement[]>();
        body!.Length.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task UpdateTenant_DeactivatesIt()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var slug = $"deact-{Guid.NewGuid():N}".Substring(0, 20);
        var create = await Client.PostAsJsonAsync("/api/v1/admin/tenants", new { name = "ToDeactivate", slug });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var resp = await Client.PutAsJsonAsync($"/api/v1/admin/tenants/{id}", new { isActive = false });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task DeleteTenant_WithNoUsers_Succeeds()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var slug = $"del-{Guid.NewGuid():N}".Substring(0, 20);
        var create = await Client.PostAsJsonAsync("/api/v1/admin/tenants", new { name = "ToDelete", slug });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var resp = await Client.DeleteAsync($"/api/v1/admin/tenants/{id}");
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteTenant_WithUsers_Returns409()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var tenantId = await CreateTenantAsync();
        await CreateUserInTenantAsync($"blocker_{Guid.NewGuid():N}@test.com", "Password1", tenantId);

        var resp = await Client.DeleteAsync($"/api/v1/admin/tenants/{tenantId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── Cross-tenant user management ──────────────────────────────────────────

    [Fact]
    public async Task AdminCreateUser_WithTenantId_Succeeds()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var tenantId = await CreateTenantAsync();
        var email    = $"admincreated_{Guid.NewGuid():N}@test.com";

        var resp = await Client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email, password = "Password1", tenantId, role = "User"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantId").GetInt32().Should().Be(tenantId);
        body.GetProperty("role").GetString().Should().Be("User");
    }

    [Fact]
    public async Task AdminCreateUser_WithoutTenantId_AsUser_Returns400()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var resp = await Client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email    = $"notenant_{Guid.NewGuid():N}@test.com",
            password = "Password1",
            role     = "User"
            // no tenantId
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AdminCreateUser_AsAdministrator_NoTenantRequired()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var email = $"newsuperadmin_{Guid.NewGuid():N}@test.com";
        var resp  = await Client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            email, password = "Password1", role = "Administrator"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("Administrator");
        body.TryGetProperty("tenantId", out var tid);
        // tenantId should be null/absent for Administrators
        (tid.ValueKind == JsonValueKind.Null || tid.ValueKind == JsonValueKind.Undefined).Should().BeTrue();
    }

    [Fact]
    public async Task AdminUpdateUser_PromotesToTenantAdmin()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var tenantId = await CreateTenantAsync();
        var email    = $"promote_{Guid.NewGuid():N}@test.com";
        var userId   = await CreateUserInTenantAsync(email, "Password1", tenantId, "User");

        var resp = await Client.PutAsJsonAsync($"/api/v1/admin/users/{userId}", new { role = "TenantAdmin" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString()
            .Should().Be("TenantAdmin");
    }

    [Fact]
    public async Task AdminLockUser_PreventsLogin()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var tenantId = await CreateTenantAsync();
        var email    = $"lockme_{Guid.NewGuid():N}@test.com";
        var userId   = await CreateUserInTenantAsync(email, "Password1", tenantId);

        (await Client.PatchAsync($"/api/v1/admin/users/{userId}/lock", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        ClearAuthHeader();
        (await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password1" }))
            .StatusCode.Should().Be((HttpStatusCode)423);
    }

    [Fact]
    public async Task AdminUnlockUser_RestoresLogin()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var tenantId = await CreateTenantAsync();
        var email    = $"unlock_{Guid.NewGuid():N}@test.com";
        var userId   = await CreateUserInTenantAsync(email, "Password1", tenantId);

        await Client.PatchAsync($"/api/v1/admin/users/{userId}/lock", null);
        await Client.PatchAsync($"/api/v1/admin/users/{userId}/unlock", null);

        ClearAuthHeader();
        (await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password1" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminDeleteUser_RemovesUserAndData()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var tenantId = await CreateTenantAsync();
        var email    = $"deleteme_{Guid.NewGuid():N}@test.com";
        var userId   = await CreateUserInTenantAsync(email, "Password1", tenantId);

        // Create a todo as that user
        var userToken = await LoginAsync(email, "Password1");
        SetAuthHeader(userToken);
        await Client.PostAsJsonAsync("/api/v1/todos", new { title = "Delete me", priority = "Low" });

        SetAuthHeader(await LoginAsSuperAdminAsync());
        (await Client.DeleteAsync($"/api/v1/admin/users/{userId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client.GetAsync($"/api/v1/admin/users/{userId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AdminDeleteSelf_Returns400()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var users = await (await Client.GetAsync("/api/v1/admin/users"))
            .Content.ReadFromJsonAsync<JsonElement[]>();
        var myId = users!.First(u => u.GetProperty("email").GetString() == SuperAdminEmail)
            .GetProperty("id").GetString()!;

        (await Client.DeleteAsync($"/api/v1/admin/users/{myId}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Auth response shape ───────────────────────────────────────────────────

    [Fact]
    public async Task Login_ResponseContainsRoleAndTenantInfo()
    {
        var tenantId = await CreateTenantAsync("Role Test Tenant");
        var email    = $"roletest_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(email, "Password1", tenantId);

        ClearAuthHeader();
        var resp = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password1" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("User");
        body.GetProperty("tenantId").GetInt32().Should().Be(tenantId);
        body.GetProperty("tenantName").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Me_ReturnsRoleAndTenantInfo()
    {
        var (token, tenantId) = await CreateTenantUserAsync();
        SetAuthHeader(token);
        var resp = await Client.GetAsync("/api/v1/auth/me");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("User");
        body.GetProperty("tenantId").GetInt32().Should().Be(tenantId);
    }

    [Fact]
    public async Task SuperAdmin_Me_HasNoTenant()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var resp = await Client.GetAsync("/api/v1/auth/me");
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("Administrator");
        // tenantId should be null
        body.TryGetProperty("tenantId", out var tid);
        (tid.ValueKind == JsonValueKind.Null || tid.ValueKind == JsonValueKind.Undefined).Should().BeTrue();
    }

    // ── Inactive tenant ───────────────────────────────────────────────────────

    [Fact]
    public async Task Login_InactiveTenant_Returns403()
    {
        SetAuthHeader(await LoginAsSuperAdminAsync());
        var tenantId = await CreateTenantAsync();
        var email    = $"inactive_{Guid.NewGuid():N}@test.com";
        await CreateUserInTenantAsync(email, "Password1", tenantId);

        // Deactivate tenant
        await Client.PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}", new { isActive = false });

        ClearAuthHeader();
        var resp = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password1" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
