namespace TodoApp.Web.Models;

public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsCompleted { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public string? Category { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public enum Priority { Low = 0, Medium = 1, High = 2 }

public class CreateTodoRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public string? Category { get; set; }
    public DateTime? DueDate { get; set; }
}

public class UpdateTodoRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsCompleted { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public string? Category { get; set; }
    public DateTime? DueDate { get; set; }
}

public class TodoStats
{
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Active { get; set; }
    public int Overdue { get; set; }
    public int TrashCount { get; set; }
}

public class AuditLogEntry
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

public class TodoQuery
{
    public bool? Completed { get; set; }
    public string? Category { get; set; }
    public Priority? Priority { get; set; }
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDesc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

// ── Admin / Tenant models ─────────────────────────────────────────────────────

public class TenantSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int UserCount { get; set; }
}

public class UserSummary
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "User";
    public DateTime CreatedAt { get; set; }
    public bool IsLocked { get; set; }
    public int? TenantId { get; set; }
    public string? TenantName { get; set; }
}

public class CreateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "User";
    public int? TenantId { get; set; }
}

public class UpdateUserRequest
{
    public string? DisplayName { get; set; }
    public string? Role { get; set; }
    public int? TenantId { get; set; }
}

public class CreateTenantRequest
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public bool? IsActive { get; set; }
}

