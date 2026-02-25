using TodoApp.Application.DTOs;

namespace TodoApp.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshTokenRequest req, CancellationToken ct = default);
    Task LogoutAsync(RefreshTokenRequest req, CancellationToken ct = default);
    Task<MeResponse> GetMeAsync(string userId, CancellationToken ct = default);
}
