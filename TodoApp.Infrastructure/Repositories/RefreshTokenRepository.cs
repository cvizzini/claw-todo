using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Interfaces;
using TodoApp.Infrastructure.Data;
using System.Security.Cryptography;

namespace TodoApp.Infrastructure.Repositories;

public class RefreshTokenRepository(TodoDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
        => db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == token, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default)
        => await db.RefreshTokens.AddAsync(token, ct);

    public Task RevokeAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.IsRevoked = true;
        return Task.CompletedTask;
    }

    public async Task PurgeExpiredAsync(string userId, CancellationToken ct = default)
    {
        var expired = await db.RefreshTokens
            .Where(t => t.UserId == userId && (t.IsRevoked || t.ExpiresAt < DateTime.UtcNow))
            .ToListAsync(ct);
        db.RefreshTokens.RemoveRange(expired);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => db.SaveChangesAsync(ct);
}
