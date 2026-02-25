using TodoApp.Domain.Entities;

namespace TodoApp.Domain.Interfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog log, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLog>> GetByTodoIdAsync(int todoId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
