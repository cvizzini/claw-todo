using TodoApp.Application.DTOs;

namespace TodoApp.Application.Interfaces;

public interface ITenantService
{
    Task<IReadOnlyList<TenantResponse>> GetAllAsync(CancellationToken ct = default);
    Task<TenantResponse?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<TenantResponse> CreateAsync(CreateTenantRequest req, CancellationToken ct = default);
    Task<TenantResponse> UpdateAsync(int id, UpdateTenantRequest req, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
