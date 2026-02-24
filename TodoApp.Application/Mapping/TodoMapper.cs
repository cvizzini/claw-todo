using TodoApp.Domain.Entities;
using TodoApp.Application.DTOs;

namespace TodoApp.Application.Mapping;

public static class TodoMapper
{
    public static TodoResponse ToResponse(this TodoItem item) => new(
        item.Id, item.Title, item.Notes, item.IsCompleted,
        item.Priority, item.Category, item.DueDate,
        item.CreatedAt, item.UpdatedAt,
        item.IsDeleted, item.DeletedAt);

    public static AuditLogResponse ToResponse(this AuditLog log) =>
        new(log.Id, log.Action, log.Details, log.Timestamp);
}
