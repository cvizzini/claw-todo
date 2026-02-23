using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Data;
using TodoApp.Api.DTOs;
using TodoApp.Api.Models;

namespace TodoApp.Api.Endpoints;

public static class TenantAdminEndpoints
{
    public static RouteGroupBuilder MapTenantAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/tenant")
            .WithTags("TenantAdmin")
            .RequireAuthorization("TenantAdminOrAbove");

        // ── GET /api/v1/tenant/me — tenant info for the caller ────────────────
        group.MapGet("/me", async (ClaimsPrincipal principal, TodoDbContext db) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();

            var tenant = await db.Tenants.FindAsync(tenantId);
            if (tenant is null) return Results.NotFound();

            return Results.Ok(new { tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt });
        })
        .WithName("TenantInfo").WithSummary("Get the current tenant's info");

        // ── GET /api/v1/tenant/users ──────────────────────────────────────────
        group.MapGet("/users", async (ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();

            var users = await userManager.Users
                .Include(u => u.Tenant)
                .Where(u => u.TenantId == tenantId)
                .OrderBy(u => u.CreatedAt)
                .ToListAsync();

            var result = new List<UserSummary>();
            foreach (var u in users)
            {
                var roles    = await userManager.GetRolesAsync(u);
                var isLocked = await userManager.IsLockedOutAsync(u);
                result.Add(new UserSummary(u.Id, u.Email ?? "", u.DisplayName,
                    roles.FirstOrDefault() ?? "User", u.CreatedAt, isLocked, u.TenantId, u.Tenant?.Name));
            }
            return Results.Ok(result);
        })
        .WithName("TenantListUsers").WithSummary("List all users within this tenant");

        // ── GET /api/v1/tenant/users/{id} ─────────────────────────────────────
        group.MapGet("/users/{id}", async (
            string id, ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();

            var user = await userManager.Users.Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
            if (user is null) return Results.NotFound();

            var roles    = await userManager.GetRolesAsync(user);
            var isLocked = await userManager.IsLockedOutAsync(user);
            return Results.Ok(new UserSummary(user.Id, user.Email ?? "", user.DisplayName,
                roles.FirstOrDefault() ?? "User", user.CreatedAt, isLocked, user.TenantId, user.Tenant?.Name));
        })
        .WithName("TenantGetUser").WithSummary("Get a user within this tenant");

        // ── POST /api/v1/tenant/users ─────────────────────────────────────────
        group.MapPost("/users", async (
            TenantCreateUserRequest req, ClaimsPrincipal principal,
            UserManager<AppUser> userManager, TodoDbContext db) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();

            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest(new { Error = "Email and password are required." });

            // TenantAdmins can only assign User or TenantAdmin within their tenant
            var role = req.Role is "TenantAdmin" or "User" ? req.Role : "User";

            if (await userManager.FindByEmailAsync(req.Email) is not null)
                return Results.Conflict(new { Error = "Email already registered." });

            var user = new AppUser
            {
                UserName    = req.Email,
                Email       = req.Email,
                DisplayName = req.DisplayName ?? req.Email.Split('@')[0],
                CreatedAt   = DateTime.UtcNow,
                TenantId    = tenantId
            };

            var result = await userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return Results.BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

            await userManager.AddToRoleAsync(user, role);

            var tenant = await db.Tenants.FindAsync(tenantId);
            return Results.Created($"/api/v1/tenant/users/{user.Id}",
                new UserSummary(user.Id, user.Email!, user.DisplayName, role, user.CreatedAt, false, tenantId, tenant?.Name));
        })
        .WithName("TenantCreateUser").WithSummary("Create a user within this tenant");

        // ── PUT /api/v1/tenant/users/{id} ─────────────────────────────────────
        group.MapPut("/users/{id}", async (
            string id, TenantUpdateUserRequest req, ClaimsPrincipal principal,
            UserManager<AppUser> userManager) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();

            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;

            // Scoped to tenant — cannot edit users outside it
            var user = await userManager.Users.Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
            if (user is null) return Results.NotFound();

            if (!string.IsNullOrWhiteSpace(req.DisplayName))
                user.DisplayName = req.DisplayName.Trim();

            if (!string.IsNullOrEmpty(req.Role) && req.Role is "TenantAdmin" or "User")
            {
                // Cannot demote yourself
                if (id == callerId && req.Role == "User")
                    return Results.BadRequest(new { Error = "You cannot remove your own TenantAdmin role." });

                var currentRoles = await userManager.GetRolesAsync(user);
                if (!currentRoles.Contains(req.Role))
                {
                    await userManager.RemoveFromRolesAsync(user, currentRoles);
                    await userManager.AddToRoleAsync(user, req.Role);
                }
            }

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return Results.BadRequest(new { Errors = updateResult.Errors.Select(e => e.Description) });

            var roles    = await userManager.GetRolesAsync(user);
            var isLocked = await userManager.IsLockedOutAsync(user);
            return Results.Ok(new UserSummary(user.Id, user.Email ?? "", user.DisplayName,
                roles.FirstOrDefault() ?? "User", user.CreatedAt, isLocked, user.TenantId, user.Tenant?.Name));
        })
        .WithName("TenantUpdateUser").WithSummary("Update a user within this tenant");

        // ── PATCH /api/v1/tenant/users/{id}/lock ──────────────────────────────
        group.MapPatch("/users/{id}/lock", async (
            string id, ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        {
            var tenantId = GetTenantId(principal);
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (tenantId is null) return Results.Forbid();
            if (id == callerId) return Results.BadRequest(new { Error = "You cannot lock your own account." });

            var user = await userManager.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
            if (user is null) return Results.NotFound();

            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
            return Results.Ok(new { Message = "User locked." });
        })
        .WithName("TenantLockUser").WithSummary("Lock a user within this tenant");

        // ── PATCH /api/v1/tenant/users/{id}/unlock ────────────────────────────
        group.MapPatch("/users/{id}/unlock", async (
            string id, ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();

            var user = await userManager.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
            if (user is null) return Results.NotFound();

            await userManager.SetLockoutEndDateAsync(user, null);
            await userManager.ResetAccessFailedCountAsync(user);
            return Results.Ok(new { Message = "User unlocked." });
        })
        .WithName("TenantUnlockUser").WithSummary("Unlock a user within this tenant");

        // ── DELETE /api/v1/tenant/users/{id} ──────────────────────────────────
        group.MapDelete("/users/{id}", async (
            string id, ClaimsPrincipal principal,
            UserManager<AppUser> userManager, TodoDbContext db) =>
        {
            var tenantId = GetTenantId(principal);
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (tenantId is null) return Results.Forbid();
            if (id == callerId) return Results.BadRequest(new { Error = "You cannot delete your own account." });

            var user = await userManager.Users
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId);
            if (user is null) return Results.NotFound();

            var todos    = await db.Todos.IgnoreQueryFilters().Where(t => t.OwnerId == id).ToListAsync();
            var todoIds  = todos.Select(t => t.Id).ToList();
            var auditLogs = await db.AuditLogs.Where(a => todoIds.Contains(a.TodoId)).ToListAsync();
            db.AuditLogs.RemoveRange(auditLogs);
            db.Todos.RemoveRange(todos);
            var tokens = await db.RefreshTokens.Where(t => t.UserId == id).ToListAsync();
            db.RefreshTokens.RemoveRange(tokens);
            await db.SaveChangesAsync();

            var result = await userManager.DeleteAsync(user);
            if (!result.Succeeded)
                return Results.BadRequest(new { Errors = result.Errors.Select(e => e.Description) });

            return Results.NoContent();
        })
        .WithName("TenantDeleteUser").WithSummary("Delete a user within this tenant");

        return group;
    }

    private static int? GetTenantId(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirstValue("tenant_id");
        return claim is not null && int.TryParse(claim, out var id) ? id : null;
    }
}
