using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Interfaces;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Infrastructure.Repositories;

public class TenantRepository(TodoDbContext db) : ITenantRepository
{
    public async Task<IReadOnlyList<Tenant>> GetAllAsync(CancellationToken ct = default)
        => await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);

    public Task<Tenant?> GetByIdAsync(int id, CancellationToken ct = default)
        => db.Tenants.FindAsync([id], ct).AsTask();

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default)
        => db.Tenants.AnyAsync(t => t.Slug == slug, ct);

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default)
        => await db.Tenants.AddAsync(tenant, ct);

    public Task DeleteAsync(Tenant tenant, CancellationToken ct = default)
    {
        db.Tenants.Remove(tenant);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
