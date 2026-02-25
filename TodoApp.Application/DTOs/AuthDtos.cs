using System.ComponentModel.DataAnnotations;

namespace TodoApp.Application.DTOs;

public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record AuthResponse(
    string Token, string RefreshToken, string Email,
    string? DisplayName, DateTime ExpiresAt, string Role,
    int? TenantId, string? TenantName);

public record RefreshTokenRequest([Required] string RefreshToken);

public record MeResponse(
    string Email, string? DisplayName, string Role,
    DateTime CreatedAt, int? TenantId, string? TenantName);
