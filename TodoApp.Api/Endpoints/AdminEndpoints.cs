using System.Security.Claims;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;

namespace TodoApp.Api.Endpoints;

public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/admin")
            .WithTags("Admin")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("api");

        // ── Tenants ───────────────────────────────────────────────────────────

        group.MapGet("/tenants", async (ITenantService tenantService) =>
            Results.Ok(await tenantService.GetAllAsync()))
        .WithName("ListTenants").WithSummary("List all tenants");

        group.MapGet("/tenants/{id:int}", async (int id, ITenantService tenantService) =>
        {
            var tenant = await tenantService.GetByIdAsync(id);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        })
        .WithName("GetTenant").WithSummary("Get a tenant by ID");

        group.MapPost("/tenants", async (CreateTenantRequest req, ITenantService tenantService) =>
        {
            var tenant = await tenantService.CreateAsync(req);
            return Results.Created($"/api/v1/admin/tenants/{tenant.Id}", tenant);
        })
        .WithName("CreateTenant").WithSummary("Create a new tenant");

        group.MapPut("/tenants/{id:int}", async (int id, UpdateTenantRequest req, ITenantService tenantService) =>
            Results.Ok(await tenantService.UpdateAsync(id, req)))
        .WithName("UpdateTenant").WithSummary("Update a tenant's name or active status");

        group.MapDelete("/tenants/{id:int}", async (int id, ITenantService tenantService) =>
        {
            await tenantService.DeleteAsync(id);
            return Results.NoContent();
        })
        .WithName("DeleteTenant").WithSummary("Delete a tenant (must have no users)");

        // ── Users ─────────────────────────────────────────────────────────────

        group.MapGet("/users", async (IUserService userService, int? tenantId) =>
            Results.Ok(await userService.GetAllAsync(tenantId)))
        .WithName("ListUsers").WithSummary("List all users, optionally filtered by tenant");

        group.MapGet("/users/{id}", async (string id, IUserService userService) =>
        {
            var user = await userService.GetByIdAsync(id);
            return user is null ? Results.NotFound() : Results.Ok(user);
        })
        .WithName("GetUser").WithSummary("Get a user by ID");

        group.MapPost("/users", async (AdminCreateUserRequest req, IUserService userService) =>
        {
            var user = await userService.CreateAsync(req);
            return Results.Created($"/api/v1/admin/users/{user.Id}", user);
        })
        .WithName("CreateUser").WithSummary("Create a new user and assign to a tenant");

        group.MapPut("/users/{id}", async (string id, AdminUpdateUserRequest req, IUserService userService) =>
            Results.Ok(await userService.UpdateAsync(id, req)))
        .WithName("UpdateUser").WithSummary("Update a user's display name, role, or tenant");

        group.MapDelete("/users/{id}", async (string id, ClaimsPrincipal principal, IUserService userService) =>
        {
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await userService.DeleteAsync(id, callerId);
            return Results.NoContent();
        })
        .WithName("DeleteUser").WithSummary("Delete a user and all their data");

        group.MapPatch("/users/{id}/lock", async (string id, ClaimsPrincipal principal, IUserService userService) =>
        {
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await userService.LockAsync(id, callerId);
            return Results.Ok(new { Message = "User locked." });
        })
        .WithName("LockUser").WithSummary("Lock a user account");

        group.MapPatch("/users/{id}/unlock", async (string id, IUserService userService) =>
        {
            await userService.UnlockAsync(id);
            return Results.Ok(new { Message = "User unlocked." });
        })
        .WithName("UnlockUser").WithSummary("Unlock a user account");

        return app;
    }
}
