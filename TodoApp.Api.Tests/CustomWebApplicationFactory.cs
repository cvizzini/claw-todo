using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Api.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly SqliteConnection _connection;

    public CustomWebApplicationFactory()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Signal to Program.cs to use EnsureCreated instead of Migrate
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=:memory:",
                ["Jwt:Key"] = "TodoApp-Super-Secret-Key-Change-In-Production-2026!",
                ["Jwt:Issuer"] = "TodoApp.Api",
                ["Jwt:Audience"] = "TodoApp.Web",
                ["Jwt:ExpiryHours"] = "8",
                ["RateLimiting:AuthLimit"] = "10000",
                ["RateLimiting:ApiLimit"] = "10000",
                // Seed super-admin so tests can authenticate as Administrator
                ["Seed:AdminEmail"]       = "admin@todoapp.local",
                ["Seed:AdminPassword"]    = "Admin1234!",
                ["Seed:AdminDisplayName"] = "Super Admin"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext options
            var descriptors = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<TodoDbContext>))
                .ToList();
            foreach (var d in descriptors)
                services.Remove(d);

            // Use the shared SQLite in-memory connection so schema persists across scopes
            services.AddDbContext<TodoDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
