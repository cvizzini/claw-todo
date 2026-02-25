namespace TodoApp.Domain.Entities;

public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsCompleted { get; set; }
    public Priority Priority { get; set; } = Priority.Medium;
    public string? Category { get; set; }
    public DateTime? DueDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string OwnerId { get; set; } = string.Empty;
}

public enum Priority { Low = 0, Medium = 1, High = 2 }
