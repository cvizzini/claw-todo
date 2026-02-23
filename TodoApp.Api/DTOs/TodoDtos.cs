using TodoApp.Api.Models;
using System.ComponentModel.DataAnnotations;

namespace TodoApp.Api.DTOs;

// ── Auth ──────────────────────────────────────────────────────────────────────
public record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record AuthResponse(
    string Token, string RefreshToken, string Email,
    string? DisplayName, DateTime ExpiresAt, string Role,
    int? TenantId, string? TenantName
);

public record RefreshTokenRequest([Required] string RefreshToken);

public record MeResponse(
    string Email, string? DisplayName, string Role,
    DateTime CreatedAt, int? TenantId, string? TenantName
);

// ── Tenant (super-admin) ──────────────────────────────────────────────────────
public record CreateTenantRequest(
    [Required, MaxLength(100)] string Name,
    [Required, MaxLength(60), RegularExpression(@"^[a-z0-9\-]+$",
        ErrorMessage = "Slug may only contain lowercase letters, numbers, and hyphens.")] string Slug
);

public record UpdateTenantRequest(
    [MaxLength(100)] string? Name,
    bool? IsActive
);

public record TenantResponse(int Id, string Name, string Slug, bool IsActive, DateTime CreatedAt, int UserCount);

// ── Admin user management ─────────────────────────────────────────────────────
public record AdminCreateUserRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [MaxLength(100)] string? DisplayName,
    int? TenantId,
    string Role = "User"
);

public record AdminUpdateUserRequest(
    [MaxLength(100)] string? DisplayName,
    string? Role,
    int? TenantId
);

public record UserSummary(
    string Id, string Email, string? DisplayName,
    string Role, DateTime CreatedAt, bool IsLockedOut,
    int? TenantId, string? TenantName
);

// ── TenantAdmin user management ───────────────────────────────────────────────
public record TenantCreateUserRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [MaxLength(100)] string? DisplayName,
    string Role = "User"   // TenantAdmin can only assign User or TenantAdmin within their tenant
);

public record TenantUpdateUserRequest(
    [MaxLength(100)] string? DisplayName,
    string? Role
);

// ── Todos ─────────────────────────────────────────────────────────────────────
public record CreateTodoRequest(
    [Required, MinLength(1), MaxLength(200)] string Title,
    [MaxLength(2000)] string? Notes,
    Priority Priority,
    [MaxLength(50)] string? Category,
    DateTime? DueDate
);

public record UpdateTodoRequest(
    [Required, MinLength(1), MaxLength(200)] string Title,
    [MaxLength(2000)] string? Notes,
    bool IsCompleted,
    Priority Priority,
    [MaxLength(50)] string? Category,
    DateTime? DueDate
);

// ── Pagination ────────────────────────────────────────────────────────────────
public record PagedResult<T>(
    IEnumerable<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasNextPage,
    bool HasPreviousPage
);

public record TodoQuery(
    bool? Completed = null,
    string? Category = null,
    Priority? Priority = null,
    string? Search = null,
    string? SortBy = null,        // createdAt | updatedAt | dueDate | priority | title
    bool SortDesc = false,
    int Page = 1,
    int PageSize = 20
);

// ── Response ──────────────────────────────────────────────────────────────────
public record TodoResponse(
    int Id,
    string Title,
    string? Notes,
    bool IsCompleted,
    Priority Priority,
    string? Category,
    DateTime? DueDate,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsDeleted,
    DateTime? DeletedAt
);

public record AuditLogResponse(int Id, string Action, string? Details, DateTime Timestamp);

public static class TodoMapper
{
    public static TodoResponse ToResponse(this TodoItem item) => new(
        item.Id,
        item.Title,
        item.Notes,
        item.IsCompleted,
        item.Priority,
        item.Category,
        item.DueDate,
        item.CreatedAt,
        item.UpdatedAt,
        item.IsDeleted,
        item.DeletedAt
    );
}
