using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;
using TodoApp.Application.Mapping;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Exceptions;
using TodoApp.Domain.Interfaces;

namespace TodoApp.Application.Services;

public class TodoService(ITodoRepository todos, IAuditLogRepository auditLogs) : ITodoService
{
    public async Task<PagedResult<TodoResponse>> GetPagedAsync(
        string ownerId, bool? completed, string? category,
        Priority? priority, string? search,
        string? sortBy, bool sortDesc, int page, int pageSize,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var (items, total) = await todos.GetPagedAsync(
            ownerId, completed, category, priority,
            search, sortBy, sortDesc, page, pageSize, ct);

        var totalPages = total == 0 ? 0 : (int)Math.Ceiling((double)total / pageSize);
        return new PagedResult<TodoResponse>(
            items.Select(t => t.ToResponse()), page, pageSize, total, totalPages,
            page < totalPages, page > 1);
    }

    public async Task<TodoResponse?> GetByIdAsync(int id, string ownerId, CancellationToken ct = default)
        => (await todos.GetByIdAsync(id, ownerId, ct))?.ToResponse();

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(string ownerId, CancellationToken ct = default)
        => await todos.GetCategoriesAsync(ownerId, ct);

    public async Task<TodoStatsResponse> GetStatsAsync(string ownerId, CancellationToken ct = default)
    {
        var (total, completed, overdue, trashCount) = await todos.GetStatsAsync(ownerId, ct);
        return new TodoStatsResponse(total, completed, total - completed, overdue, trashCount);
    }

    public async Task<IReadOnlyList<TodoResponse>> GetTrashAsync(string ownerId, CancellationToken ct = default)
        => (await todos.GetTrashAsync(ownerId, ct)).Select(t => t.ToResponse()).ToList();

    public async Task<IReadOnlyList<AuditLogResponse>> GetAuditLogAsync(int todoId, string ownerId, CancellationToken ct = default)
    {
        // Check ownership — allow deleted todos too
        var exists = await todos.GetByIdAsync(todoId, ownerId, ct)
            ?? (object?)await todos.GetDeletedByIdAsync(todoId, ownerId, ct);
        if (exists is null) throw new NotFoundException("Todo not found.");

        var logs = await auditLogs.GetByTodoIdAsync(todoId, ct);
        return logs.Select(l => l.ToResponse()).ToList();
    }

    public async Task<TodoResponse> CreateAsync(string ownerId, CreateTodoRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ValidationException("Title is required.");

        var todo = new TodoItem
        {
            Title = req.Title.Trim(), Notes = req.Notes?.Trim(),
            Priority = req.Priority, Category = req.Category?.Trim(),
            DueDate = req.DueDate, OwnerId = ownerId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };

        await todos.AddAsync(todo, ct);
        await todos.SaveChangesAsync(ct);

        await auditLogs.AddAsync(new AuditLog
        {
            TodoId = todo.Id, UserId = ownerId,
            Action = "Created", Details = $"Created: \"{todo.Title}\""
        }, ct);
        await auditLogs.SaveChangesAsync(ct);

        return todo.ToResponse();
    }

    public async Task<TodoResponse> UpdateAsync(int id, string ownerId, UpdateTodoRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ValidationException("Title is required.");

        var todo = await todos.GetByIdAsync(id, ownerId, ct)
            ?? throw new NotFoundException("Todo not found.");

        var changes = new List<string>();
        if (todo.Title != req.Title.Trim())         changes.Add($"Title: \"{todo.Title}\" → \"{req.Title.Trim()}\"");
        if (todo.IsCompleted != req.IsCompleted)    changes.Add($"Completed: {todo.IsCompleted} → {req.IsCompleted}");
        if (todo.Priority != req.Priority)          changes.Add($"Priority: {todo.Priority} → {req.Priority}");
        if (todo.Category != req.Category?.Trim())  changes.Add($"Category: \"{todo.Category}\" → \"{req.Category?.Trim()}\"");

        todo.Title = req.Title.Trim();       todo.Notes = req.Notes?.Trim();
        todo.IsCompleted = req.IsCompleted;  todo.Priority = req.Priority;
        todo.Category = req.Category?.Trim(); todo.DueDate = req.DueDate;
        todo.UpdatedAt = DateTime.UtcNow;

        await auditLogs.AddAsync(new AuditLog
        {
            TodoId = todo.Id, UserId = ownerId, Action = "Updated",
            Details = changes.Count > 0 ? string.Join("; ", changes) : "No field changes"
        }, ct);

        await todos.SaveChangesAsync(ct);
        return todo.ToResponse();
    }

    public async Task<TodoResponse> ToggleAsync(int id, string ownerId, CancellationToken ct = default)
    {
        var todo = await todos.GetByIdAsync(id, ownerId, ct)
            ?? throw new NotFoundException("Todo not found.");

        todo.IsCompleted = !todo.IsCompleted;
        todo.UpdatedAt = DateTime.UtcNow;

        await auditLogs.AddAsync(new AuditLog
        {
            TodoId = todo.Id, UserId = ownerId,
            Action = todo.IsCompleted ? "Completed" : "Reopened",
            Details = todo.IsCompleted ? "Marked as complete" : "Marked as active"
        }, ct);

        await todos.SaveChangesAsync(ct);
        return todo.ToResponse();
    }

    public async Task SoftDeleteAsync(int id, string ownerId, CancellationToken ct = default)
    {
        var todo = await todos.GetByIdAsync(id, ownerId, ct)
            ?? throw new NotFoundException("Todo not found.");

        todo.IsDeleted = true;
        todo.DeletedAt = DateTime.UtcNow;

        await auditLogs.AddAsync(new AuditLog
        {
            TodoId = todo.Id, UserId = ownerId,
            Action = "Deleted", Details = $"Moved to trash: \"{todo.Title}\""
        }, ct);

        await todos.SaveChangesAsync(ct);
    }

    public async Task RestoreAsync(int id, string ownerId, CancellationToken ct = default)
    {
        var todo = await todos.GetDeletedByIdAsync(id, ownerId, ct)
            ?? throw new NotFoundException("Todo not found in trash.");

        todo.IsDeleted = false;
        todo.DeletedAt = null;
        todo.UpdatedAt = DateTime.UtcNow;

        await auditLogs.AddAsync(new AuditLog
        {
            TodoId = todo.Id, UserId = ownerId,
            Action = "Restored", Details = $"Restored from trash: \"{todo.Title}\""
        }, ct);

        await todos.SaveChangesAsync(ct);
    }

    public async Task PermanentDeleteAsync(int id, string ownerId, CancellationToken ct = default)
    {
        var todo = await todos.GetDeletedByIdAsync(id, ownerId, ct)
            ?? throw new NotFoundException("Todo not found in trash.");

        await todos.HardDeleteAsync(todo, ct);
    }

    public Task<int> EmptyTrashAsync(string ownerId, CancellationToken ct = default)
        => todos.ClearTrashAsync(ownerId, ct);

    public Task<int> ClearCompletedAsync(string ownerId, CancellationToken ct = default)
        => todos.SoftDeleteCompletedAsync(ownerId, ct);
}
