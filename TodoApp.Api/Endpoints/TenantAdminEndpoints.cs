using System.Security.Claims;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;

namespace TodoApp.Api.Endpoints;

public static class TenantAdminEndpoints
{
    public static WebApplication MapTenantAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/tenant")
            .WithTags("TenantAdmin")
            .RequireAuthorization("TenantAdminOrAbove")
            .RequireRateLimiting("api");

        group.MapGet("/me", async (ClaimsPrincipal principal, ITenantService tenantService) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();
            var tenant = await tenantService.GetByIdAsync(tenantId.Value);
            return tenant is null ? Results.NotFound() : Results.Ok(tenant);
        })
        .WithName("TenantInfo").WithSummary("Get the current tenant's info");

        group.MapGet("/users", async (ClaimsPrincipal principal, IUserService userService) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();
            return Results.Ok(await userService.GetByTenantAsync(tenantId.Value));
        })
        .WithName("TenantListUsers").WithSummary("List all users within this tenant");

        group.MapGet("/users/{id}", async (string id, ClaimsPrincipal principal, IUserService userService) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();
            var user = await userService.GetByIdInTenantAsync(id, tenantId.Value);
            return user is null ? Results.NotFound() : Results.Ok(user);
        })
        .WithName("TenantGetUser").WithSummary("Get a user within this tenant");

        group.MapPost("/users", async (TenantCreateUserRequest req, ClaimsPrincipal principal, IUserService userService) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();
            var user = await userService.CreateInTenantAsync(tenantId.Value, req);
            return Results.Created($"/api/v1/tenant/users/{user.Id}", user);
        })
        .WithName("TenantCreateUser").WithSummary("Create a user within this tenant");

        group.MapPut("/users/{id}", async (string id, TenantUpdateUserRequest req, ClaimsPrincipal principal, IUserService userService) =>
        {
            var tenantId = GetTenantId(principal);
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (tenantId is null) return Results.Forbid();
            return Results.Ok(await userService.UpdateInTenantAsync(tenantId.Value, id, req, callerId));
        })
        .WithName("TenantUpdateUser").WithSummary("Update a user within this tenant");

        group.MapPatch("/users/{id}/lock", async (string id, ClaimsPrincipal principal, IUserService userService) =>
        {
            var tenantId = GetTenantId(principal);
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (tenantId is null) return Results.Forbid();
            await userService.LockInTenantAsync(tenantId.Value, id, callerId);
            return Results.Ok(new { Message = "User locked." });
        })
        .WithName("TenantLockUser").WithSummary("Lock a user within this tenant");

        group.MapPatch("/users/{id}/unlock", async (string id, ClaimsPrincipal principal, IUserService userService) =>
        {
            var tenantId = GetTenantId(principal);
            if (tenantId is null) return Results.Forbid();
            await userService.UnlockInTenantAsync(tenantId.Value, id);
            return Results.Ok(new { Message = "User unlocked." });
        })
        .WithName("TenantUnlockUser").WithSummary("Unlock a user within this tenant");

        group.MapDelete("/users/{id}", async (string id, ClaimsPrincipal principal, IUserService userService) =>
        {
            var tenantId = GetTenantId(principal);
            var callerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (tenantId is null) return Results.Forbid();
            await userService.DeleteInTenantAsync(tenantId.Value, id, callerId);
            return Results.NoContent();
        })
        .WithName("TenantDeleteUser").WithSummary("Delete a user within this tenant");

        return app;
    }

    private static int? GetTenantId(ClaimsPrincipal principal)
    {
        var claim = principal.FindFirstValue("tenant_id");
        return claim is not null && int.TryParse(claim, out var id) ? id : null;
    }
}
