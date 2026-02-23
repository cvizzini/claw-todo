using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Data;
using TodoApp.Api.DTOs;
using TodoApp.Api.Models;

namespace TodoApp.Api.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly");

        // ── Tenants ───────────────────────────────────────────────────────────

        group.MapGet("/tenants", async (TodoDbContext db, UserManager<AppUser> userManager) =>
        {
            var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
            var result  = new List<TenantResponse>();
            foreach (var t in tenants)
            {
                var count = await userManager.Users.CountAsync(u => u.TenantId == t.Id);
                result.Add(new TenantResponse(t.Id, t.Name, t.Slug, t.IsActive, t.CreatedAt, count));
            }
            return Results.Ok(result);
        })
        .WithName("AdminListTenants").WithSummary("List all tenants");

        group.MapGet("/tenants/{id:int}", async (int id, TodoDbContext db, UserManager<AppUser> userManager) =>
        {
            var t = await db.Tenants.FindAsync(id);
            if (t is null) return Results.NotFound();
            var count = await userManager.Users.CountAsync(u => u.TenantId == id);
            return Results.Ok(new TenantResponse(t.Id, t.Name, t.Slug, t.IsActive, t.CreatedAt, count));
        })
        .WithName("AdminGetTenant").WithSummary("Get a tenant by ID");

        group.MapPost("/tenants", async (CreateTenantRequest req, TodoDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Slug))
                return Results.BadRequest(new { Error = "Name and slug are required." });

            var slug = req.Slug.ToLowerInvariant().Trim();
            if (!Regex.IsMatch(slug, @"^[a-z0-9\-]+$"))
                return Results.BadRequest(new { Error = "Slug may only contain lowercase letters, numbers, and hyphens." });

            if (await db.Tenants.AnyAsync(t => t.Slug == slug))
                return Results.Conflict(new { Error = "A tenant with that slug already exists." });

            var tenant = new Tenant { Name = req.Name.Trim(), Slug = slug, CreatedAt = DateTime.UtcNow };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            return Results.Created($"/api/v1/admin/tenants/{tenant.Id}",
                new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt, 0));
        })
        .WithName("AdminCreateTenant").WithSummary("Create a new tenant");

        group.MapPut("/tenants/{id:int}", async (int id, UpdateTenantRequest req, TodoDbContext db) =>
        {
            var tenant = await db.Tenants.FindAsync(id);
            if (tenant is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(req.Name)) tenant.Name = req.Name.Trim();
            if (req.IsActive.HasValue)                 tenant.IsActive = req.IsActive.Value;

            await db.SaveChangesAsync();
            var count = 0; // skip count on update for perf
            return Results.Ok(new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt, count));
        })
        .WithName("AdminUpdateTenant").WithSummary("Update a tenant's name or active status");

        group.MapDelete("/tenants/{id:int}", async (int id, TodoDbContext db, UserManager<AppUser> userManager) =>
        {
            var tenant = await db.Tenants.FindAsync(id);
            if (tenant is null) return Results.NotFound();

            var hasUsers = await userManager.Users.AnyAsync(u => u.TenantId == id);
            if (hasUsers)
                return Results.Conflict(new { Error = "Cannot delete a tenant that still has users. Remove or reassign users first." });

            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("AdminDeleteTenant").WithSummary("Delete an empty tenant");

        // ── Users (cross-tenant) ──────────────────────────────────────────────

        group.MapGet("/users", async (UserManager<AppUser> userManager, TodoDbContext db, int? tenantId) =>
        {
            var query = userManager.Users.AsNoTracking().Include(u => u.Tenant).AsQueryable();
            if (tenantId.HasValue) query = query.Where(u => u.TenantId == tenantId);
            var users = await query.OrderBy(u => u.CreatedAt).ToListAsync();
            return Results.Ok(await ToUserSummaries(users, userManager));
        })
        .WithName("AdminListUsers").WithSummary("List all users (optionally filtered by tenantId)");

        group.MapGet("/users/{id}", async (string id, UserManager<AppUser> userManager) =>
        {
            var user = await userManager.Users.AsNoTracking().Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();
            var roles    = await userManager.GetRolesAsync(user);
            var isLocked = await userManager.IsLockedOutAsync(user);
            return Results.Ok(ToUserSummary(user, roles, isLocked));
        })
        .WithName("AdminGetUser").WithSummary("Get a user by ID");

        group.MapPost("/users", async (AdminCreateUserRequest req, UserManager<AppUser> userManager, TodoDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { Error = "Email and password are required." });

            var role = req.Role is "Administrator" or "TenantAdmin" or "User" ? req.Role : "User";

            // Non-Administrator roles must belong to a tenant
            if (role != "Administrator" && req.TenantId is null)
                return Results.BadRequest(new { Error = "TenantId is required for User and TenantAdmin roles." });

            if (req.TenantId.HasValue && !await db.Tenants.AnyAsync(t => t.Id == req.TenantId && t.IsActive))
                return Results.BadRequest(new { Error = "Tenant not found or is inactive." });

            if (await userManager.FindByEmailAsync(req.Email) is not null)
                return Results.Conflict(new { Error = "Email already registered." });

            var user = new AppUser
            {
                UserName    = req.Email,
                Email       = req.Email,
                DisplayName = req.DisplayName ?? req.Email.Split('@')[0],
                CreatedAt   = DateTime.UtcNow,
                TenantId    = role == "Administrator" ? null : req.TenantId
            };

            var result = await userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Results.BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

            await userManager.AddToRoleAsync(user, role);

            var tenant = user.TenantId.HasValue ? await db.Tenants.FindAsync(user.TenantId) : null;
            return Results.Created($"/api/v1/admin/users/{user.Id}",
                new UserSummary(user.Id, user.Email!, user.DisplayName, role, user.CreatedAt, false, user.TenantId, tenant?.Name));
        })
        .WithName("AdminCreateUser").WithSummary("Create a user (with role and optional tenant)");

        group.MapPut("/users/{id}", async (string id, AdminUpdateUserRequest req, UserManager<AppUser> userManager, TodoDbContext db) =>
        {
            var user = await userManager.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.Id == id);
            if (user is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(req.DisplayName))
                user.DisplayName = req.DisplayName.Trim();

            if (req.TenantId.HasValue)
            {
                if (!await db.Tenants.AnyAsync(t => t.Id == req.TenantId))
                    return Results.BadRequest(new { Error = "Tenant not found." });
                user.TenantId = req.TenantId;
            }

            if (!string.IsNullOrEmpty(req.Role) && req.Role is "Administrator" or "TenantAdmin" or "User")
            {
                var currentRoles = await userManager.GetRolesAsync(user);
                if (!currentRoles.Contains(req.Role))
                {
                    await userManager.RemoveFromRolesAsync(user, currentRoles);
                    await userManager.AddToRoleAsync(user, req.Role);
                }
                // Super-admins have no tenant
                if (req.Role == "Administrator") user.TenantId = null;
            }

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return Results.BadRequest(new { Errors = updateResult.Errors.Select(e => e.Description) });

            var roles    = await userManager.GetRolesAsync(user);
            var isLocked = await userManager.IsLockedOutAsync(user);
            // Reload tenant after potential change
            var tenant   = user.TenantId.HasValue ? await db.Tenants.FindAsync(user.TenantId) : null;
            return Results.Ok(new UserSummary(user.Id, user.Email ?? "", user.DisplayName,
                roles.FirstOrDefault() ?? "User", user.CreatedAt, isLocked, user.TenantId, tenant?.Name));
        })
        .WithName("AdminUpdateUser").WithSummary("Update a user's details, role, or tenant");

        group.MapDelete("/users/{id}", async (
            string id, ClaimsPrincipal principal,
            UserManager<AppUser> userManager, TodoDbContext db) =>
        {
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (id == callerId)
                return Results.BadRequest(new { Error = "You cannot delete your own account." });

            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();

            var todos    = await db.Todos.IgnoreQueryFilters().Where(t => t.OwnerId == id).ToListAsync();
            var todoIds  = todos.Select(t => t.Id).ToList();
            var auditLogs = await db.AuditLogs.Where(a => todoIds.Contains(a.TodoId)).ToListAsync();
            db.AuditLogs.RemoveRange(auditLogs);
            db.Todos.RemoveRange(todos);

            var tokens = await db.RefreshTokens.Where(t => t.UserId == id).ToListAsync();
            db.RefreshTokens.RemoveRange(tokens);
            await db.SaveChangesAsync();

            var deleteResult = await userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
                return Results.BadRequest(new { Errors = deleteResult.Errors.Select(e => e.Description) });

            return Results.NoContent();
        })
        .WithName("AdminDeleteUser").WithSummary("Delete a user and all their data");

        group.MapPatch("/users/{id}/lock", async (string id, ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        {
            if (id == principal.FindFirstValue(ClaimTypes.NameIdentifier))
                return Results.BadRequest(new { Error = "You cannot lock your own account." });

            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            return Results.Ok(new { Message = "User locked." });
        })
        .WithName("AdminLockUser").WithSummary("Lock a user account");

        group.MapPatch("/users/{id}/unlock", async (string id, UserManager<AppUser> userManager) =>
        {
            var user = await userManager.FindByIdAsync(id);
            if (user is null) return Results.NotFound();
            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);
            return Results.Ok(new { Message = "User unlocked." });
        })
        .WithName("AdminUnlockUser").WithSummary("Unlock a user account");

        return group;
    }

    private static UserSummary ToUserSummary(AppUser user, IList<string> roles, bool isLocked) =>
        new(user.Id, user.Email ?? "", user.DisplayName,
            roles.FirstOrDefault() ?? "User", user.CreatedAt,
            isLocked, user.TenantId, user.Tenant?.Name);

    private static async Task<List<UserSummary>> ToUserSummaries(
        IEnumerable<AppUser> users, UserManager<AppUser> userManager)
    {
        var result = new List<UserSummary>();
        foreach (var u in users)
        {
            var roles    = await userManager.GetRolesAsync(u);
            var isLocked = await userManager.IsLockedOutAsync(u);
            result.Add(ToUserSummary(u, roles, isLocked));
        }
        return result;
    }
}
