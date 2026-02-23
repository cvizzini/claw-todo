using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Api.Data;
using TodoApp.Api.Models;

namespace TodoApp.Api.Tests;

public abstract class TestBase : IClassFixture<CustomWebApplicationFactory>
{
    protected readonly HttpClient Client;
    protected readonly CustomWebApplicationFactory Factory;
    protected static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Shared super-admin credentials (seeded by appsettings Seed config)
    protected const string SuperAdminEmail    = "admin@todoapp.local";
    protected const string SuperAdminPassword = "Admin1234!";

    protected TestBase(CustomWebApplicationFactory factory)
    {
        Factory = factory;
        Client  = factory.CreateClient();
    }

    /// <summary>
    /// Creates a fresh tenant and a User within it. Returns the user's JWT.
    /// </summary>
    protected async Task<(string token, int tenantId)> CreateTenantUserAsync(
        string? email = null, string password = "Password1", string role = "User")
    {
        email ??= $"user_{Guid.NewGuid():N}@test.com";
        var tenantId = await CreateTenantAsync();
        await CreateUserInTenantAsync(email, password, tenantId, role);
        var token = await LoginAsync(email, password);
        return (token, tenantId);
    }

    /// <summary>
    /// Creates a fresh tenant. Returns its ID.
    /// </summary>
    protected async Task<int> CreateTenantAsync(string? name = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db     = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var slug   = $"tenant-{Guid.NewGuid():N}";
        var tenant = new Tenant { Name = name ?? $"Test Tenant {slug}", Slug = slug };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>
    /// Creates a user in the given tenant with the specified role. Returns their ID.
    /// </summary>
    protected async Task<string> CreateUserInTenantAsync(
        string email, string password, int tenantId, string role = "User")
    {
        using var scope       = Factory.Services.CreateScope();
        var userManager       = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null) return existing.Id;

        var user = new AppUser
        {
            UserName    = email,
            Email       = email,
            DisplayName = email.Split('@')[0],
            TenantId    = tenantId,
            CreatedAt   = DateTime.UtcNow
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not create user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        await userManager.AddToRoleAsync(user, role);
        return user.Id;
    }

    /// <summary>
    /// Gets a JWT for the super-admin.
    /// </summary>
    protected async Task<string> LoginAsSuperAdminAsync()
        => await LoginAsync(SuperAdminEmail, SuperAdminPassword);

    /// <summary>
    /// Gets a JWT via the login endpoint.
    /// </summary>
    protected async Task<string> LoginAsync(string email, string password)
    {
        var resp = await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return doc.GetProperty("token").GetString()!;
    }

    /// <summary>
    /// Legacy helper — kept so existing todo tests compile without changes.
    /// Creates a fresh tenant+user and returns their JWT.
    /// </summary>
    protected async Task<string> RegisterAndLoginAsync(string? email = null, string password = "Password1")
    {
        var (token, _) = await CreateTenantUserAsync(email, password);
        return token;
    }

    protected void SetAuthHeader(string token)
        => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    protected void ClearAuthHeader()
        => Client.DefaultRequestHeaders.Authorization = null;
}
