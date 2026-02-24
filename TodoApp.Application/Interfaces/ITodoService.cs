using TodoApp.Application.DTOs;
using TodoApp.Domain.Entities;

namespace TodoApp.Application.Interfaces;

public interface ITodoService
{
    Task<PagedResult<TodoResponse>> GetPagedAsync(
        string ownerId, bool? completed, string? category,
        Priority? priority, string? search,
        string? sortBy, bool sortDesc, int page, int pageSize,
        CancellationToken ct = default);

    Task<TodoResponse?> GetByIdAsync(int id, string ownerId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(string ownerId, CancellationToken ct = default);
    Task<TodoStatsResponse> GetStatsAsync(string ownerId, CancellationToken ct = default);
    Task<IReadOnlyList<TodoResponse>> GetTrashAsync(string ownerId, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogResponse>> GetAuditLogAsync(int todoId, string ownerId, CancellationToken ct = default);
    Task<TodoResponse> CreateAsync(string ownerId, CreateTodoRequest req, CancellationToken ct = default);
    Task<TodoResponse> UpdateAsync(int id, string ownerId, UpdateTodoRequest req, CancellationToken ct = default);
    Task<TodoResponse> ToggleAsync(int id, string ownerId, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, string ownerId, CancellationToken ct = default);
    Task RestoreAsync(int id, string ownerId, CancellationToken ct = default);
    Task PermanentDeleteAsync(int id, string ownerId, CancellationToken ct = default);
    Task<int> EmptyTrashAsync(string ownerId, CancellationToken ct = default);
    Task<int> ClearCompletedAsync(string ownerId, CancellationToken ct = default);
}
