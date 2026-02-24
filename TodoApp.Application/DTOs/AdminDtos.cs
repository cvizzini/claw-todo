using System.ComponentModel.DataAnnotations;

namespace TodoApp.Application.DTOs;

public record TenantResponse(int Id, string Name, string Slug, bool IsActive, DateTime CreatedAt, int UserCount);

public record CreateTenantRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(60)] string Slug);

public record UpdateTenantRequest(
    [MaxLength(100)] string? Name,
    bool? IsActive);

public record UserSummary(
    string Id, string Email, string? DisplayName,
    string Role, DateTime CreatedAt, bool IsLockedOut,
    int? TenantId, string? TenantName);

public record AdminCreateUserRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [MaxLength(100)] string? DisplayName,
    int? TenantId,
    string Role = "User");

public record AdminUpdateUserRequest(
    [MaxLength(100)] string? DisplayName,
    string? Role,
    int? TenantId);

public record TenantCreateUserRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [MaxLength(100)] string? DisplayName,
    string Role = "User");

public record TenantUpdateUserRequest(
    [MaxLength(100)] string? DisplayName,
    string? Role);
