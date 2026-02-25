using TodoApp.Domain.Entities;

namespace TodoApp.Domain.Interfaces;

public interface ITenantRepository
{
    Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct = default);
    Task<Tenant?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);
    Task AddAsync(Tenant tenant, CancellationToken ct = default);
    Task DeleteAsync(Tenant tenant, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
