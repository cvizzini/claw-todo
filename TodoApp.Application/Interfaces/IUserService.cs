using TodoApp.Application.DTOs;

namespace TodoApp.Application.Interfaces;

public interface IUserService
{
    // Super-admin operations (cross-tenant)
    Task<IReadOnlyList<UserSummary>> GetAllAsync(int? tenantId = null, CancellationToken ct = default);
    Task<UserSummary?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<UserSummary> CreateAsync(AdminCreateUserRequest req, CancellationToken ct = default);
    Task<UserSummary> UpdateAsync(string id, AdminUpdateUserRequest req, CancellationToken ct = default);
    Task DeleteAsync(string id, string callerId, CancellationToken ct = default);
    Task LockAsync(string id, string callerId, CancellationToken ct = default);
    Task UnlockAsync(string id, CancellationToken ct = default);

    // Tenant-scoped operations
    Task<IReadOnlyList<UserSummary>> GetByTenantAsync(int tenantId, CancellationToken ct = default);
    Task<UserSummary?> GetByIdInTenantAsync(string id, int tenantId, CancellationToken ct = default);
    Task<UserSummary> CreateInTenantAsync(int tenantId, TenantCreateUserRequest req, CancellationToken ct = default);
    Task<UserSummary> UpdateInTenantAsync(int tenantId, string id, TenantUpdateUserRequest req, string callerId, CancellationToken ct = default);
    Task DeleteInTenantAsync(int tenantId, string id, string callerId, CancellationToken ct = default);
    Task LockInTenantAsync(int tenantId, string id, string callerId, CancellationToken ct = default);
    Task UnlockInTenantAsync(int tenantId, string id, CancellationToken ct = default);
}
