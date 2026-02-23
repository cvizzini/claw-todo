using System.Diagnostics;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using TodoApp.Api.Data;
using TodoApp.Api.Endpoints;
using TodoApp.Api.Middleware;

// ── Serilog bootstrap ─────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/todoapi-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "TodoApp.Api")
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
        .WriteTo.File(
            path: "logs/todoapi-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"));

    // ── Database ──────────────────────────────────────────────────────────────
    builder.Services.AddDbContext<TodoDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
            ?? "Data Source=todos.db"));

    // ── Identity ──────────────────────────────────────────────────────────────
    builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<TodoDbContext>()
    .AddDefaultTokenProviders();

    // ── JWT Authentication ────────────────────────────────────────────────────
    var jwtSection = builder.Configuration.GetSection("Jwt");
    var jwtKey = jwtSection["Key"]!;

    builder.Services.AddAuthentication(options =>
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

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly",           policy => policy.RequireRole("Administrator"));
        options.AddPolicy("TenantAdminOrAbove",  policy => policy.RequireRole("Administrator", "TenantAdmin"));
    });

    // ── Rate Limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        var authLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthLimit", 10);
        var apiLimit  = builder.Configuration.GetValue<int>("RateLimiting:ApiLimit", 100);

        // Auth endpoints: configurable per minute per IP (default 10)
        options.AddPolicy("auth", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit       = authLimit,
                    Window            = TimeSpan.FromMinutes(1),
                    QueueLimit        = 0,
                    AutoReplenishment = true
                }));

        // General API: configurable per minute per IP (default 100)
        options.AddPolicy("api", context =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit       = apiLimit,
                    Window            = TimeSpan.FromMinutes(1),
                    QueueLimit        = 0,
                    AutoReplenishment = true
                }));

        options.OnRejected = async (ctx, token) =>
        {
            ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ctx.HttpContext.Response.ContentType = "application/json";
            await ctx.HttpContext.Response.WriteAsync(
                "{\"error\":\"Too many requests. Please slow down.\",\"retryAfter\":60}", token);
        };
    });

    // ── OpenAPI ───────────────────────────────────────────────────────────────
    builder.Services.AddOpenApi("v1", options =>
    {
        options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    });

    // ── CORS ──────────────────────────────────────────────────────────────────
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("BlazorFrontend", policy =>
            policy.WithOrigins("http://localhost:5121", "https://localhost:7064")
                  .AllowAnyMethod()
                  .AllowAnyHeader());
    });

    // ── Problem Details ───────────────────────────────────────────────────────
    builder.Services.AddProblemDetails();

    // ── Health Checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<TodoDbContext>(
            name:          "database",
            failureStatus: HealthStatus.Unhealthy,
            tags:          ["db", "ready"]);

    // ── OpenTelemetry ─────────────────────────────────────────────────────────
    var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"]; // e.g. http://localhost:4317
    var serviceName  = builder.Configuration["OpenTelemetry:ServiceName"] ?? "TodoApp.Api";

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(res => res
            .AddService(serviceName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = builder.Environment.EnvironmentName,
                ["service.version"]        = "1.0.0"
            }))
        .WithTracing(tracing =>
        {
            tracing
                .AddAspNetCoreInstrumentation(opts =>
                {
                    // Skip health check endpoints — they would just be noise
                    opts.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health") &&
                        !ctx.Request.Path.StartsWithSegments("/health/ready");
                })
                .AddEntityFrameworkCoreInstrumentation(opts =>
                {
                    // Include full SQL text in spans — disable in prod if queries contain PII
                    opts.SetDbStatementForText = true;
                });

            if (!string.IsNullOrEmpty(otelEndpoint))
                tracing.AddOtlpExporter(opts => opts.Endpoint = new Uri(otelEndpoint));
            else if (builder.Environment.IsDevelopment())
                tracing.AddConsoleExporter();
        })
        .WithMetrics(metrics =>
        {
            metrics
                .AddAspNetCoreInstrumentation()  // http.server.request.duration, active requests, etc.
                .AddRuntimeInstrumentation();    // GC collections, heap size, thread pool queue

            if (!string.IsNullOrEmpty(otelEndpoint))
                metrics.AddOtlpExporter(opts => opts.Endpoint = new Uri(otelEndpoint));
            else if (builder.Environment.IsDevelopment())
                metrics.AddConsoleExporter();
        });

    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
    builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

    var app = builder.Build();

    // ── Migrate on startup ────────────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db  = scope.ServiceProvider.GetRequiredService<TodoDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        if (!env.IsEnvironment("Test"))
            db.Database.Migrate();
        else
            db.Database.EnsureCreated();
    }

    // ── Seed roles ────────────────────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "User", "TenantAdmin", "Administrator" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // ── Seed super-admin ──────────────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var seedSection = app.Configuration.GetSection("Seed");
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

    // ── Middleware pipeline ───────────────────────────────────────────────────
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        opts.GetLevel = (ctx, elapsed, ex) => ex != null || ctx.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : ctx.Response.StatusCode >= 400
                ? LogEventLevel.Warning
                : LogEventLevel.Information;

        // Enrich each request log with tenant + trace context
        opts.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
        {
            diagCtx.Set("RequestHost",   httpCtx.Request.Host.Value);
            diagCtx.Set("UserAgent",     httpCtx.Request.Headers.UserAgent.ToString());
            diagCtx.Set("UserId",        httpCtx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "-");
            diagCtx.Set("TenantId",      httpCtx.User.FindFirst("tenant_id")?.Value ?? "-");
            // Include OTel trace/span IDs so logs can be correlated to traces
            var activity = Activity.Current;
            if (activity is not null)
            {
                diagCtx.Set("TraceId", activity.TraceId.ToString());
                diagCtx.Set("SpanId",  activity.SpanId.ToString());
            }
        };
    });

    app.UseCors("BlazorFrontend");
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // ── Scalar API docs (dev/test only) ──────────────────────────────────────
    app.MapOpenApi();
    if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
    {
        app.MapScalarApiReference(options =>
        {
            options.Title  = "Todo API v1";
            options.Theme  = ScalarTheme.Purple;
            options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
            options.Authentication = new ScalarAuthenticationOptions
            {
                PreferredSecuritySchemes = ["Bearer"]
            };
        });
    }

    // ── Endpoints ─────────────────────────────────────────────────────────────
    app.MapAuthEndpoints();
    app.MapTodoEndpoints();
    app.MapAdminEndpoints();
    app.MapTenantAdminEndpoints();

    // ── Health endpoints ──────────────────────────────────────────────────────
    // GET /health/live  — liveness: is the process alive? (no DB check, used by container orchestrators)
    app.MapGet("/health/live", () => Results.Ok(new { Status = "alive", Time = DateTime.UtcNow }))
       .WithTags("Health")
       .AllowAnonymous()
       .ExcludeFromDescription();

    // GET /health/ready — readiness: is the app ready to serve (DB reachable)?
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate      = check => check.Tags.Contains("ready"),
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            var result = new
            {
                status  = report.Status.ToString(),
                checks  = report.Entries.Select(e => new
                {
                    name     = e.Key,
                    status   = e.Value.Status.ToString(),
                    duration = e.Value.Duration.TotalMilliseconds
                }),
                totalDuration = report.TotalDuration.TotalMilliseconds
            };
            await ctx.Response.WriteAsJsonAsync(result);
        }
    })
    .AllowAnonymous()
    .WithTags("Health");

    // GET /health — backwards-compatible alias that includes both live + ready info
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            var result = new
            {
                status        = report.Status.ToString(),
                time          = DateTime.UtcNow,
                checks        = report.Entries.Select(e => new
                {
                    name     = e.Key,
                    status   = e.Value.Status.ToString(),
                    duration = e.Value.Duration.TotalMilliseconds
                }),
                totalDuration = report.TotalDuration.TotalMilliseconds
            };
            await ctx.Response.WriteAsJsonAsync(result);
        }
    })
    .AllowAnonymous()
    .WithTags("Health");

    Log.Information("TodoApp API starting up");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// ── Bearer security scheme transformer ───────────────────────────────────────
internal sealed class BearerSecuritySchemeTransformer(
    Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider authSchemeProvider)
    : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var authSchemes = await authSchemeProvider.GetAllSchemesAsync();
        if (!authSchemes.Any(s => s.Name == JwtBearerDefaults.AuthenticationScheme))
            return;

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type         = SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            Description  = "Paste the JWT from POST /api/v1/auth/login"
        };
    }
}

public partial class Program { }
