using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TodoApp.Domain.Entities;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Infrastructure.Data;

public class TodoDbContext : IdentityDbContext<AppUser>
{
    public TodoDbContext(DbContextOptions<TodoDbContext> options) : base(options) { }

    public DbSet<TodoItem> Todos => Set<TodoItem>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Tenant> Tenants => Set<Tenant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Slug).IsRequired().HasMaxLength(60);
            entity.HasIndex(e => e.Slug).IsUnique();
            // Relationship is configured on AppUser side (Infrastructure knows both)
            entity.HasMany<AppUser>()
                  .WithOne(u => u.Tenant)
                  .HasForeignKey(u => u.TenantId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(e => e.TenantId);
        });

        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.Category).HasMaxLength(50);
            entity.Property(e => e.Priority).HasConversion<string>();
            entity.Property(e => e.OwnerId).IsRequired();
            entity.HasIndex(e => e.OwnerId);
            entity.HasIndex(e => new { e.OwnerId, e.IsCompleted });
            entity.HasIndex(e => new { e.OwnerId, e.Category });
            entity.HasIndex(e => new { e.OwnerId, e.IsDeleted });
            entity.HasQueryFilter(t => !t.IsDeleted);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Token).IsRequired().HasMaxLength(512);
            entity.Property(e => e.UserId).IsRequired();
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Details).HasMaxLength(1000);
            entity.HasIndex(e => e.TodoId);
            entity.HasIndex(e => e.UserId);
        });
    }
}
