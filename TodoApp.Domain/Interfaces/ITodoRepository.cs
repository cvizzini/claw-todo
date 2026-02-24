using TodoApp.Domain.Entities;

namespace TodoApp.Domain.Interfaces;

public interface ITodoRepository
{
    Task<(IReadOnlyList<TodoItem> Items, int TotalCount)> GetPagedAsync(
        string ownerId, bool? completed, string? category,
        Priority? priority, string? search,
        string? sortBy, bool sortDesc, int page, int pageSize,
        CancellationToken ct = default);

    Task<TodoItem?> GetByIdAsync(int id, string ownerId, CancellationToken ct = default);
    Task<TodoItem?> GetDeletedByIdAsync(int id, string ownerId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(string ownerId, CancellationToken ct = default);
    Task<(int Total, int Completed, int Overdue, int TrashCount)> GetStatsAsync(string ownerId, CancellationToken ct = default);
    Task<IReadOnlyList<TodoItem>> GetTrashAsync(string ownerId, CancellationToken ct = default);
    Task AddAsync(TodoItem todo, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
    Task<int> ClearTrashAsync(string ownerId, CancellationToken ct = default);
    Task<int> SoftDeleteCompletedAsync(string ownerId, CancellationToken ct = default);
    Task HardDeleteAsync(TodoItem todo, CancellationToken ct = default);
}
