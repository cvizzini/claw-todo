using System.ComponentModel.DataAnnotations;
using TodoApp.Domain.Entities;

namespace TodoApp.Application.DTOs;

public record CreateTodoRequest(
    [Required, MinLength(1), MaxLength(200)] string Title,
    [MaxLength(2000)] string? Notes,
    Priority Priority,
    [MaxLength(50)] string? Category,
    DateTime? DueDate);

public record UpdateTodoRequest(
    [Required, MinLength(1), MaxLength(200)] string Title,
    [MaxLength(2000)] string? Notes,
    bool IsCompleted,
    Priority Priority,
    [MaxLength(50)] string? Category,
    DateTime? DueDate);

public record TodoResponse(
    int Id, string Title, string? Notes, bool IsCompleted,
    Priority Priority, string? Category, DateTime? DueDate,
    DateTime CreatedAt, DateTime UpdatedAt,
    bool IsDeleted, DateTime? DeletedAt);

public record AuditLogResponse(int Id, string Action, string? Details, DateTime Timestamp);

public record PagedResult<T>(
    IEnumerable<T> Items, int Page, int PageSize,
    int TotalCount, int TotalPages,
    bool HasNextPage, bool HasPreviousPage);

public record TodoStatsResponse(int Total, int Completed, int Active, int Overdue, int TrashCount);
