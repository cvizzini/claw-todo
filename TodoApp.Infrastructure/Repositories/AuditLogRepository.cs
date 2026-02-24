using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Interfaces;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Infrastructure.Repositories;

public class AuditLogRepository(TodoDbContext db) : IAuditLogRepository
{
    public async Task AddAsync(AuditLog log, CancellationToken ct = default)
        => await db.AuditLogs.AddAsync(log, ct);

    public async Task<IReadOnlyList<AuditLog>> GetByTodoIdAsync(int todoId, CancellationToken ct = default)
        => await db.AuditLogs.AsNoTracking()
            .Where(a => a.TodoId == todoId)
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
