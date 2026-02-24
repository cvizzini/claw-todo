using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using TodoApp.Application.Interfaces;
using TodoApp.Domain.Interfaces;
using TodoApp.Infrastructure.Data;
using TodoApp.Infrastructure.Identity;
using TodoApp.Infrastructure.Repositories;
using TodoApp.Infrastructure.Services;

namespace TodoApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── Database ──────────────────────────────────────────────────────────
        services.AddDbContext<TodoDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=todos.db"));

        // ── ASP.NET Core Identity ─────────────────────────────────────────────
        services.AddIdentity<AppUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit           = true;
            options.Password.RequiredLength         = 6;
            options.Password.RequireUppercase       = false;
            options.Password.RequireNonAlphanumeric = false;
            options.User.RequireUniqueEmail         = true;
        })
        .AddEntityFrameworkStores<TodoDbContext>()
        .AddDefaultTokenProviders();

        // ── JWT Authentication ────────────────────────────────────────────────
        var jwtSection = configuration.GetSection("Jwt");
        var jwtKey     = jwtSection["Key"]!;

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer           = true,
                ValidateAudience         = true,
                ValidateLifetime         = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer              = jwtSection["Issuer"],
                ValidAudience            = jwtSection["Audience"],
                IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew                = TimeSpan.FromSeconds(30)
            };
        });

        // ── Repositories ──────────────────────────────────────────────────────
        services.AddScoped<ITodoRepository,         TodoRepository>();
        services.AddScoped<IAuditLogRepository,     AuditLogRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ITenantRepository,       TenantRepository>();

        // ── Infrastructure services ───────────────────────────────────────────
        services.AddScoped<ITokenService,    TokenService>();
        services.AddScoped<IAppUserAccessor, AppUserAccessor>();
        services.AddScoped<IUserService,     UserService>();

        return services;
    }
}
