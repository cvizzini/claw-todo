namespace TodoApp.Application.Interfaces;

public interface ITokenService
{
    (string Token, DateTime Expires) GenerateAccessToken(
        string userId, string email, string? displayName,
        IList<string> roles, int? tenantId);

    Task<string> CreateRefreshTokenAsync(string userId, CancellationToken ct = default);
}
