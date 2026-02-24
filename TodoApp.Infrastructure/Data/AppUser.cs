using Microsoft.AspNetCore.Identity;
using TodoApp.Domain.Entities;

namespace TodoApp.Infrastructure.Data;

public class AppUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
}
