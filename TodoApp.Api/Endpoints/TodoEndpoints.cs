using System.Security.Claims;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;
using TodoApp.Domain.Entities;

namespace TodoApp.Api.Endpoints;

public static class TodoEndpoints
{
    public static WebApplication MapTodoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/todos")
            .WithTags("Todos")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        group.MapGet("/", async (
            ClaimsPrincipal principal, ITodoService todoService,
            bool? completed, string? category, Priority? priority,
            string? search, string? sortBy, bool sortDesc = false,
            int page = 1, int pageSize = 20) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result  = await todoService.GetPagedAsync(ownerId, completed, category, priority, search, sortBy, sortDesc, page, pageSize);
            return Results.Ok(result);
        })
        .WithName("GetTodos").WithSummary("List todos with pagination, search, and filters");

        group.MapGet("/stats", async (ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return Results.Ok(await todoService.GetStatsAsync(ownerId));
        })
        .WithName("GetTodoStats").WithSummary("Get todo counts by status");

        group.MapGet("/categories", async (ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return Results.Ok(await todoService.GetCategoriesAsync(ownerId));
        })
        .WithName("GetCategories").WithSummary("Get distinct categories used by the current user");

        group.MapGet("/trash", async (ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            return Results.Ok(await todoService.GetTrashAsync(ownerId));
        })
        .WithName("GetTrash").WithSummary("List soft-deleted todos");

        group.MapDelete("/trash", async (ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var count   = await todoService.EmptyTrashAsync(ownerId);
            return Results.Ok(new { Deleted = count });
        })
        .WithName("EmptyTrash").WithSummary("Permanently delete all trashed todos");

        group.MapDelete("/completed", async (ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var count   = await todoService.ClearCompletedAsync(ownerId);
            return Results.Ok(new { Deleted = count });
        })
        .WithName("ClearCompleted").WithSummary("Soft-delete all completed todos");

        group.MapGet("/{id:int}", async (int id, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var item    = await todoService.GetByIdAsync(id, ownerId);
            return item is null ? Results.NotFound() : Results.Ok(item);
        })
        .WithName("GetTodoById").WithSummary("Get a single todo by ID");

        group.MapPost("/", async (CreateTodoRequest req, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var created = await todoService.CreateAsync(ownerId, req);
            return Results.Created($"/api/v1/todos/{created.Id}", created);
        })
        .WithName("CreateTodo").WithSummary("Create a new todo");

        group.MapPut("/{id:int}", async (int id, UpdateTodoRequest req, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var updated = await todoService.UpdateAsync(id, ownerId, req);
            return Results.Ok(updated);
        })
        .WithName("UpdateTodo").WithSummary("Update a todo");

        group.MapPatch("/{id:int}/toggle", async (int id, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var result  = await todoService.ToggleAsync(id, ownerId);
            return Results.Ok(result);
        })
        .WithName("ToggleTodo").WithSummary("Toggle the completed state of a todo");

        group.MapDelete("/{id:int}", async (int id, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await todoService.SoftDeleteAsync(id, ownerId);
            return Results.NoContent();
        })
        .WithName("DeleteTodo").WithSummary("Soft-delete a todo (moves to trash)");

        group.MapPost("/{id:int}/restore", async (int id, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await todoService.RestoreAsync(id, ownerId);
            return Results.Ok(new { Message = "Todo restored." });
        })
        .WithName("RestoreTodo").WithSummary("Restore a todo from trash");

        group.MapDelete("/{id:int}/permanent", async (int id, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            await todoService.PermanentDeleteAsync(id, ownerId);
            return Results.NoContent();
        })
        .WithName("PermanentDeleteTodo").WithSummary("Permanently delete a todo from trash");

        group.MapGet("/{id:int}/audit", async (int id, ClaimsPrincipal principal, ITodoService todoService) =>
        {
            var ownerId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var logs    = await todoService.GetAuditLogAsync(id, ownerId);
            return Results.Ok(logs);
        })
        .WithName("GetTodoAuditLog").WithSummary("Get the audit history for a todo");

        return app;
    }
}
