using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Interfaces;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Infrastructure.Repositories;

public class TodoRepository(TodoDbContext db) : ITodoRepository
{
    public async Task<(IReadOnlyList<TodoItem> Items, int TotalCount)> GetPagedAsync(
        string ownerId, bool? completed, string? category,
        Priority? priority, string? search,
        string? sortBy, bool sortDesc, int page, int pageSize,
        CancellationToken ct = default)
    {
        var query = db.Todos.AsNoTracking().Where(t => t.OwnerId == ownerId);

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

        var totalCount = await query.CountAsync(ct);

        query = sortBy?.ToLower() switch
        {
            "title"     => sortDesc ? query.OrderByDescending(t => t.Title)     : query.OrderBy(t => t.Title),
            "updatedat" => sortDesc ? query.OrderByDescending(t => t.UpdatedAt) : query.OrderBy(t => t.UpdatedAt),
            "duedate"   => sortDesc ? query.OrderByDescending(t => t.DueDate)   : query.OrderBy(t => t.DueDate),
            "priority"  => sortDesc ? query.OrderByDescending(t => t.Priority)  : query.OrderBy(t => t.Priority),
            _           => query.OrderByDescending(t => t.Priority).ThenBy(t => t.IsCompleted).ThenByDescending(t => t.CreatedAt)
        };

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, totalCount);
    }

    public Task<TodoItem?> GetByIdAsync(int id, string ownerId, CancellationToken ct = default)
        => db.Todos.FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, ct);

    public Task<TodoItem?> GetDeletedByIdAsync(int id, string ownerId, CancellationToken ct = default)
        => db.Todos.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId && t.IsDeleted, ct);

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(string ownerId, CancellationToken ct = default)
        => await db.Todos.AsNoTracking()
            .Where(t => t.OwnerId == ownerId && t.Category != null)
            .Select(t => t.Category!).Distinct().OrderBy(c => c).ToListAsync(ct);

    public async Task<(int Total, int Completed, int Overdue, int TrashCount)> GetStatsAsync(
        string ownerId, CancellationToken ct = default)
    {
        var total     = await db.Todos.AsNoTracking().CountAsync(t => t.OwnerId == ownerId, ct);
        var completed = await db.Todos.AsNoTracking().CountAsync(t => t.OwnerId == ownerId && t.IsCompleted, ct);
        var overdue   = await db.Todos.AsNoTracking().CountAsync(t => t.OwnerId == ownerId && !t.IsCompleted && t.DueDate < DateTime.UtcNow, ct);
        var trash     = await db.Todos.IgnoreQueryFilters().AsNoTracking().CountAsync(t => t.OwnerId == ownerId && t.IsDeleted, ct);
        return (total, completed, overdue, trash);
    }

    public async Task<IReadOnlyList<TodoItem>> GetTrashAsync(string ownerId, CancellationToken ct = default)
        => await db.Todos.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.OwnerId == ownerId && t.IsDeleted)
            .OrderByDescending(t => t.DeletedAt)
            .ToListAsync(ct);

    public async Task AddAsync(TodoItem todo, CancellationToken ct = default)
        => await db.Todos.AddAsync(todo, ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);

    public async Task<int> ClearTrashAsync(string ownerId, CancellationToken ct = default)
    {
        var trashItems = await db.Todos.IgnoreQueryFilters()
            .Where(t => t.OwnerId == ownerId && t.IsDeleted).ToListAsync(ct);

        if (trashItems.Count > 0)
        {
            var ids = trashItems.Select(t => t.Id).ToList();
            var logs = await db.AuditLogs.Where(a => ids.Contains(a.TodoId)).ToListAsync(ct);
            db.AuditLogs.RemoveRange(logs);
            db.Todos.RemoveRange(trashItems);
            await db.SaveChangesAsync(ct);
        }
        return trashItems.Count;
    }

    public async Task<int> SoftDeleteCompletedAsync(string ownerId, CancellationToken ct = default)
    {
        var completed = await db.Todos.Where(t => t.OwnerId == ownerId && t.IsCompleted).ToListAsync(ct);
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
        await db.SaveChangesAsync(ct);
        return completed.Count;
    }

    public async Task HardDeleteAsync(TodoItem todo, CancellationToken ct = default)
    {
        var logs = await db.AuditLogs.Where(a => a.TodoId == todo.Id).ToListAsync(ct);
        db.AuditLogs.RemoveRange(logs);
        db.Todos.Remove(todo);
        await db.SaveChangesAsync(ct);
    }
}
