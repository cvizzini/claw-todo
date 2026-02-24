using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using TodoApp.Web.Models;

namespace TodoApp.Web.Services;

public class AuthService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly JsonSerializerOptions _json;

    public AuthService(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js   = js;
        _json = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public AuthState? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null && CurrentUser.ExpiresAt > DateTime.UtcNow;
    public bool IsAdmin         => IsAuthenticated && CurrentUser!.Role is "Administrator";
    public bool IsTenantAdmin   => IsAuthenticated && CurrentUser!.Role is "TenantAdmin" or "Administrator";

    // Guards against calling TryRestoreSessionAsync multiple times in one circuit
    private bool _sessionRestored = false;

    public event Action? AuthStateChanged;

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<(bool Success, string? Error)> LoginAsync(string email, string password)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/api/v1/auth/login",
                new { Email = email, Password = password });

            if ((int)response.StatusCode == 423)
                return (false, "Your account is locked. Please contact your administrator.");

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, "Your organisation account is inactive. Please contact support.");

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, "Invalid email or password.");

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                return (false, "Too many login attempts. Please wait a minute and try again.");

            if (!response.IsSuccessStatusCode)
                return (false, "Login failed. Please try again.");

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_json);
            if (auth is null) return (false, "Invalid response from server.");

            await SetAuthAsync(auth);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Connection error: {ex.Message}");
        }
    }

    // ── Token refresh ─────────────────────────────────────────────────────────

    public async Task<bool> TryRefreshAsync()
    {
        if (CurrentUser is null || string.IsNullOrEmpty(CurrentUser.RefreshToken))
            return false;

        try
        {
            var response = await _http.PostAsJsonAsync("/api/v1/auth/refresh",
                new { RefreshToken = CurrentUser.RefreshToken });

            if (!response.IsSuccessStatusCode)
            {
                await ClearAuthAsync();
                return false;
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_json);
            if (auth is null) { await ClearAuthAsync(); return false; }

            await SetAuthAsync(auth);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ── Restore from localStorage on page load ────────────────────────────────

    public async Task TryRestoreSessionAsync()
    {
        if (_sessionRestored) return;
        _sessionRestored = true;

        try
        {
            var stored = await _js.InvokeAsync<StoredSession?>("sessionStore.load");
            if (stored is null) return;

            // If the access token is still valid, restore directly
            if (stored.ExpiresAt > DateTime.UtcNow.AddMinutes(1))
            {
                CurrentUser = new AuthState(
                    stored.Email, stored.DisplayName, stored.Token,
                    stored.RefreshToken, stored.ExpiresAt,
                    stored.Role, stored.TenantId, stored.TenantName);

                _http.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", stored.Token);

                AuthStateChanged?.Invoke();
                return;
            }

            // Token expired but we have a refresh token — try silent refresh
            // Temporarily set state so TryRefreshAsync can read the refresh token
            CurrentUser = new AuthState(
                stored.Email, stored.DisplayName, stored.Token,
                stored.RefreshToken, stored.ExpiresAt,
                stored.Role, stored.TenantId, stored.TenantName);

            await TryRefreshAsync();
        }
        catch
        {
            // localStorage unavailable (SSR prerender) — safe to ignore
        }
    }

    // ── Logout ────────────────────────────────────────────────────────────────

    public async Task LogoutAsync()
    {
        if (CurrentUser?.RefreshToken is not null)
        {
            try
            {
                await _http.PostAsJsonAsync("/api/v1/auth/logout",
                    new { RefreshToken = CurrentUser.RefreshToken });
            }
            catch { /* best effort */ }
        }

        await ClearAuthAsync();
    }

    // Keep sync shim for code that can't await
    public void Logout() => _ = LogoutAsync();

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task SetAuthAsync(AuthResponse auth)
    {
        CurrentUser = new AuthState(
            auth.Email, auth.DisplayName, auth.Token, auth.RefreshToken,
            auth.ExpiresAt, auth.Role, auth.TenantId, auth.TenantName);

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.Token);

        try
        {
            await _js.InvokeVoidAsync("sessionStore.save", new StoredSession(
                auth.Email, auth.DisplayName, auth.Token, auth.RefreshToken,
                auth.ExpiresAt, auth.Role, auth.TenantId, auth.TenantName));
        }
        catch { /* JS not available during prerender */ }

        AuthStateChanged?.Invoke();
    }

    private async Task ClearAuthAsync()
    {
        CurrentUser = null;
        _sessionRestored = false;
        _http.DefaultRequestHeaders.Authorization = null;

        try { await _js.InvokeVoidAsync("sessionStore.clear"); }
        catch { }

        AuthStateChanged?.Invoke();
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record AuthResponse(
        string Token, string RefreshToken, string Email, string? DisplayName,
        DateTime ExpiresAt, string? Role, int? TenantId, string? TenantName);

    private record StoredSession(
        string Email, string? DisplayName, string Token, string RefreshToken,
        DateTime ExpiresAt, string? Role, int? TenantId, string? TenantName);
}

public record AuthState(
    string Email, string? DisplayName, string Token, string RefreshToken,
    DateTime ExpiresAt, string? Role, int? TenantId, string? TenantName);
