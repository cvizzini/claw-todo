using TodoApp.Domain.Entities;

namespace TodoApp.Domain.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default);
    Task AddAsync(RefreshToken token, CancellationToken ct = default);
    Task RevokeAsync(RefreshToken token, CancellationToken ct = default);
    Task PurgeExpiredAsync(string userId, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
