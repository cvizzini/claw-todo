using System.Text.RegularExpressions;
using TodoApp.Application.DTOs;
using TodoApp.Application.Interfaces;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Exceptions;
using TodoApp.Domain.Interfaces;

namespace TodoApp.Application.Services;

public class TenantService(ITenantRepository tenants, IUserService users) : ITenantService
{
    public async Task<IReadOnlyList<TenantResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await tenants.GetAllAsync(ct);
        var result = new List<TenantResponse>();
        foreach (var t in all)
        {
            var tenantUsers = await users.GetByTenantAsync(t.Id, ct);
            result.Add(new TenantResponse(t.Id, t.Name, t.Slug, t.IsActive, t.CreatedAt, tenantUsers.Count));
        }
        return result;
    }

    public async Task<TenantResponse?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var t = await tenants.GetByIdAsync(id, ct);
        if (t is null) return null;
        var tenantUsers = await users.GetByTenantAsync(id, ct);
        return new TenantResponse(t.Id, t.Name, t.Slug, t.IsActive, t.CreatedAt, tenantUsers.Count);
    }

    public async Task<TenantResponse> CreateAsync(CreateTenantRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Slug))
            throw new ValidationException("Name and slug are required.");

        var slug = req.Slug.ToLowerInvariant().Trim();
        if (!Regex.IsMatch(slug, @"^[a-z0-9\-]+$"))
            throw new ValidationException("Slug may only contain lowercase letters, numbers, and hyphens.");

        if (await tenants.SlugExistsAsync(slug, ct))
            throw new ConflictException("A tenant with that slug already exists.");

        var tenant = new Tenant { Name = req.Name.Trim(), Slug = slug, CreatedAt = DateTime.UtcNow };
        await tenants.AddAsync(tenant, ct);
        await tenants.SaveChangesAsync(ct);
        return new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt, 0);
    }

    public async Task<TenantResponse> UpdateAsync(int id, UpdateTenantRequest req, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Tenant not found.");

        if (!string.IsNullOrWhiteSpace(req.Name)) tenant.Name = req.Name.Trim();
        if (req.IsActive.HasValue) tenant.IsActive = req.IsActive.Value;

        await tenants.SaveChangesAsync(ct);
        return new TenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt, 0);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(id, ct)
            ?? throw new NotFoundException("Tenant not found.");

        var tenantUsers = await users.GetByTenantAsync(id, ct);
        if (tenantUsers.Count > 0)
            throw new ConflictException("Cannot delete a tenant that still has users. Remove or reassign users first.");

        await tenants.DeleteAsync(tenant, ct);
        await tenants.SaveChangesAsync(ct);
    }
}
