using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TodoApp.Infrastructure.Data;

namespace TodoApp.Infrastructure.Seeding;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        // Seed roles
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "User", "TenantAdmin", "Administrator" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed super-admin
        var userManager = services.GetRequiredService<UserManager<AppUser>>();
        var seedSection = configuration.GetSection("Seed");
        var adminEmail  = seedSection["AdminEmail"];
        var adminPass   = seedSection["AdminPassword"];
        var adminName   = seedSection["AdminDisplayName"] ?? "Super Admin";

        if (!string.IsNullOrEmpty(adminEmail) && !string.IsNullOrEmpty(adminPass))
        {
            var existing = await userManager.FindByEmailAsync(adminEmail);
            if (existing is null)
            {
                var superAdmin = new AppUser
                {
                    UserName    = adminEmail,
                    Email       = adminEmail,
                    DisplayName = adminName,
                    TenantId    = null,
                    CreatedAt   = DateTime.UtcNow
                };
                var result = await userManager.CreateAsync(superAdmin, adminPass);
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(superAdmin, "Administrator");
            }
        }
    }
}
