using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TodoApp.Api.Data;
using TodoApp.Api.DTOs;
using TodoApp.Api.Models;

namespace TodoApp.Api.Endpoints;

public static class TodoEndpoints
{
    public static RouteGroupBuilder MapTodoEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/todos")
            .WithTags("Todos")
            .RequireAuthorization();

        // ── GET /api/v1/todos ─────────────────────────────────────────────────
        group.MapGet("/", async (
            TodoDbContext db, ClaimsPrincipal user,
            bool? completed, string? category, Priority? priority,
            string? search, string? sortBy, bool sortDesc = false,
            int page = 1, int pageSize = 20) =>
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            page     = Math.Max(1, page);

            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var query   = db.Todos.AsNoTracking().Where(t => t.OwnerId == ownerId);

            if (completed.HasValue)              query = query.Where(t => t.IsCompleted == completed.Value);
            if (!string.IsNullOrEmpty(category)) query = query.Where(t => t.Category == category);
            if (priority.HasValue)               query = query.Where(t => t.Priority == priority.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.ToLower();
                query = query.Where(t =>
                    t.Title.ToLower().Contains(q) ||
                    (t.Notes != null && t.Notes.ToLower().Contains(q)) ||
                    (t.Category != null && t.Category.ToLower().Contains(q)));
            }

            var totalCount = await query.CountAsync();

            query = sortBy?.ToLower() switch
            {
                "title"     => sortDesc ? query.OrderByDescending(t => t.Title)     : query.OrderBy(t => t.Title),
                "updatedat" => sortDesc ? query.OrderByDescending(t => t.UpdatedAt) : query.OrderBy(t => t.UpdatedAt),
                "duedate"   => sortDesc ? query.OrderByDescending(t => t.DueDate)   : query.OrderBy(t => t.DueDate),
                "priority"  => sortDesc ? query.OrderByDescending(t => t.Priority)  : query.OrderBy(t => t.Priority),
                _           => query.OrderByDescending(t => t.Priority).ThenBy(t => t.IsCompleted).ThenByDescending(t => t.CreatedAt)
            };

            var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(t => t.ToResponse()).ToListAsync();

            var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling((double)totalCount / pageSize);

            return Results.Ok(new PagedResult<TodoResponse>(
                Items: items, Page: page, PageSize: pageSize,
                TotalCount: totalCount, TotalPages: totalPages,
                HasNextPage: page < totalPages, HasPreviousPage: page > 1));
        })
        .WithName("GetTodos").WithSummary("Get paginated todos with optional filtering and search");

        // ── GET /api/v1/todos/categories ──────────────────────────────────────
        group.MapGet("/categories", async (TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var cats = await db.Todos
                .AsNoTracking()
                .Where(t => t.OwnerId == ownerId && t.Category != null)
                .Select(t => t.Category!).Distinct().OrderBy(c => c).ToListAsync();
            return Results.Ok(cats);
        })
        .WithName("GetCategories").WithSummary("Get distinct categories for the current user");

        // ── GET /api/v1/todos/stats ───────────────────────────────────────────
        group.MapGet("/stats", async (TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId   = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var total     = await db.Todos.AsNoTracking().CountAsync(t => t.OwnerId == ownerId);
            var completed = await db.Todos.AsNoTracking().CountAsync(t => t.OwnerId == ownerId && t.IsCompleted);
            var overdue   = await db.Todos.AsNoTracking().CountAsync(t => t.OwnerId == ownerId && !t.IsCompleted && t.DueDate < DateTime.UtcNow);
            var trashCount = await db.Todos.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(t => t.OwnerId == ownerId && t.IsDeleted);
            return Results.Ok(new
            {
                Total = total, Completed = completed,
                Active = total - completed, Overdue = overdue,
                TrashCount = trashCount
            });
        })
        .WithName("GetStats").WithSummary("Get todo counts for the current user");

        // ── GET /api/v1/todos/trash ───────────────────────────────────────────
        group.MapGet("/trash", async (TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var items = await db.Todos.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.OwnerId == ownerId && t.IsDeleted)
                .OrderByDescending(t => t.DeletedAt)
                .Select(t => t.ToResponse())
                .ToListAsync();
            return Results.Ok(items);
        })
        .WithName("GetTrash").WithSummary("Get soft-deleted todos (trash)");

        // ── GET /api/v1/todos/{id} ────────────────────────────────────────────
        group.MapGet("/{id:int}", async (int id, TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var todo    = await db.Todos.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId);
            return todo is null ? Results.NotFound() : Results.Ok(todo.ToResponse());
        })
        .WithName("GetTodoById").WithSummary("Get a single todo by ID");

        // ── GET /api/v1/todos/{id}/audit ──────────────────────────────────────
        group.MapGet("/{id:int}/audit", async (int id, TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            // Verify ownership (use IgnoreQueryFilters so we can audit deleted todos too)
            var exists = await db.Todos.IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(t => t.Id == id && t.OwnerId == ownerId);
            if (!exists) return Results.NotFound();

            var logs = await db.AuditLogs
                .AsNoTracking()
                .Where(a => a.TodoId == id)
                .OrderByDescending(a => a.Timestamp)
                .Select(a => new AuditLogResponse(a.Id, a.Action, a.Details, a.Timestamp))
                .ToListAsync();
            return Results.Ok(logs);
        })
        .WithName("GetTodoAudit").WithSummary("Get audit log for a todo");

        // ── POST /api/v1/todos ────────────────────────────────────────────────
        group.MapPost("/", async (CreateTodoRequest req, TodoDbContext db, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return Results.BadRequest(new { Error = "Title is required." });

            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var todo = new TodoItem
            {
                Title = req.Title.Trim(), Notes = req.Notes?.Trim(),
                Priority = req.Priority, Category = req.Category?.Trim(),
                DueDate = req.DueDate, OwnerId = ownerId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            };

            db.Todos.Add(todo);
            await db.SaveChangesAsync();

            db.AuditLogs.Add(new AuditLog
            {
                TodoId = todo.Id, UserId = ownerId,
                Action = "Created", Details = $"Created: \"{todo.Title}\""
            });
            await db.SaveChangesAsync();

            return Results.Created($"/api/v1/todos/{todo.Id}", todo.ToResponse());
        })
        .WithName("CreateTodo").WithSummary("Create a new todo");

        // ── PUT /api/v1/todos/{id} ────────────────────────────────────────────
        group.MapPut("/{id:int}", async (int id, UpdateTodoRequest req, TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var todo    = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId);
            if (todo is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest(new { Error = "Title is required." });

            var changes = new List<string>();
            if (todo.Title != req.Title.Trim())       changes.Add($"Title: \"{todo.Title}\" → \"{req.Title.Trim()}\"");
            if (todo.IsCompleted != req.IsCompleted)  changes.Add($"Completed: {todo.IsCompleted} → {req.IsCompleted}");
            if (todo.Priority != req.Priority)        changes.Add($"Priority: {todo.Priority} → {req.Priority}");
            if (todo.Category != req.Category?.Trim()) changes.Add($"Category: \"{todo.Category}\" → \"{req.Category?.Trim()}\"");

            todo.Title = req.Title.Trim(); todo.Notes = req.Notes?.Trim();
            todo.IsCompleted = req.IsCompleted; todo.Priority = req.Priority;
            todo.Category = req.Category?.Trim(); todo.DueDate = req.DueDate;
            todo.UpdatedAt = DateTime.UtcNow;

            db.AuditLogs.Add(new AuditLog
            {
                TodoId = todo.Id, UserId = ownerId,
                Action = "Updated",
                Details = changes.Any() ? string.Join("; ", changes) : "No field changes"
            });

            await db.SaveChangesAsync();
            return Results.Ok(todo.ToResponse());
        })
        .WithName("UpdateTodo").WithSummary("Update an existing todo");

        // ── PATCH /api/v1/todos/{id}/toggle ───────────────────────────────────
        group.MapPatch("/{id:int}/toggle", async (int id, TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var todo    = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId);
            if (todo is null) return Results.NotFound();

            todo.IsCompleted = !todo.IsCompleted;
            todo.UpdatedAt   = DateTime.UtcNow;

            db.AuditLogs.Add(new AuditLog
            {
                TodoId = todo.Id, UserId = ownerId,
                Action = todo.IsCompleted ? "Completed" : "Reopened",
                Details = todo.IsCompleted ? "Marked as complete" : "Marked as active"
            });

            await db.SaveChangesAsync();
            return Results.Ok(todo.ToResponse());
        })
        .WithName("ToggleTodo").WithSummary("Toggle the completed state");

        // ── DELETE /api/v1/todos/{id} — SOFT DELETE ───────────────────────────
        group.MapDelete("/{id:int}", async (int id, TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var todo    = await db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId);
            if (todo is null) return Results.NotFound();

            todo.IsDeleted = true;
            todo.DeletedAt = DateTime.UtcNow;

            db.AuditLogs.Add(new AuditLog
            {
                TodoId = todo.Id, UserId = ownerId,
                Action = "Deleted", Details = $"Moved to trash: \"{todo.Title}\""
            });

            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("DeleteTodo").WithSummary("Soft-delete a todo (moves to trash)");

        // ── POST /api/v1/todos/{id}/restore ───────────────────────────────────
        group.MapPost("/{id:int}/restore", async (int id, TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var todo    = await db.Todos.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId && t.IsDeleted);
            if (todo is null) return Results.NotFound();

            todo.IsDeleted = false;
            todo.DeletedAt = null;
            todo.UpdatedAt = DateTime.UtcNow;

            db.AuditLogs.Add(new AuditLog
            {
                TodoId = todo.Id, UserId = ownerId,
                Action = "Restored", Details = $"Restored from trash: \"{todo.Title}\""
            });

            await db.SaveChangesAsync();
            return Results.Ok(todo.ToResponse());
        })
        .WithName("RestoreTodo").WithSummary("Restore a soft-deleted todo from trash");

        // ── DELETE /api/v1/todos/{id}/permanent ───────────────────────────────
        group.MapDelete("/{id:int}/permanent", async (int id, TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var todo    = await db.Todos.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId && t.IsDeleted);
            if (todo is null) return Results.NotFound();

            // Hard delete audit logs for this todo too
            var logs = await db.AuditLogs.Where(a => a.TodoId == id).ToListAsync();
            db.AuditLogs.RemoveRange(logs);
            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
            return Results.NoContent();
        })
        .WithName("PermanentDeleteTodo").WithSummary("Permanently hard-delete a todo from trash");

        // ── DELETE /api/v1/todos/trash ────────────────────────────────────────
        group.MapDelete("/trash", async (TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var trashItems = await db.Todos.IgnoreQueryFilters()
                .Where(t => t.OwnerId == ownerId && t.IsDeleted).ToListAsync();

            if (trashItems.Any())
            {
                var trashIds = trashItems.Select(t => t.Id).ToList();
                var logs = await db.AuditLogs.Where(a => trashIds.Contains(a.TodoId)).ToListAsync();
                db.AuditLogs.RemoveRange(logs);
                db.Todos.RemoveRange(trashItems);
                await db.SaveChangesAsync();
            }

            return Results.Ok(new { Deleted = trashItems.Count });
        })
        .WithName("EmptyTrash").WithSummary("Permanently delete all trashed todos");

        // ── DELETE /api/v1/todos/completed — SOFT DELETE ──────────────────────
        group.MapDelete("/completed", async (TodoDbContext db, ClaimsPrincipal user) =>
        {
            var ownerId   = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var completed = await db.Todos.Where(t => t.OwnerId == ownerId && t.IsCompleted).ToListAsync();

            foreach (var t in completed)
            {
                t.IsDeleted = true;
                t.DeletedAt = DateTime.UtcNow;
                db.AuditLogs.Add(new AuditLog
                {
                    TodoId = t.Id, UserId = ownerId,
                    Action = "Deleted", Details = "Cleared with completed batch delete"
                });
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { Deleted = completed.Count });
        })
        .WithName("ClearCompleted").WithSummary("Soft-delete all completed todos");

        return group;
    }
}
