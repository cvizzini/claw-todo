namespace TodoApp.Api.Models;

public class AuditLog
{
    public int Id { get; set; }
    public int TodoId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // Created, Updated, Completed, Restored, Deleted, PermanentlyDeleted
    public string? Details { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
