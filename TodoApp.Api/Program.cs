using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;
using TodoApp.Api.Endpoints;
using TodoApp.Api.Middleware;
using TodoApp.Application;
using TodoApp.Infrastructure;
using TodoApp.Infrastructure.Data;
using TodoApp.Infrastructure.Seeding;

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

    // ── Infrastructure (DB, Identity, JWT, Repositories, Services) ────────────
    builder.Services.AddInfrastructure(builder.Configuration);

    // ── Application (Todo/Auth/Tenant services) ───────────────────────────────
    builder.Services.AddApplication();

    // ── Authorization policies ────────────────────────────────────────────────
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("AdminOnly",          policy => policy.RequireRole("Administrator"));
        options.AddPolicy("TenantAdminOrAbove", policy => policy.RequireRole("Administrator", "TenantAdmin"));
    });

    // ── Rate Limiting ─────────────────────────────────────────────────────────
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        var authLimit = builder.Configuration.GetValue<int>("RateLimiting:AuthLimit", 10);
        var apiLimit  = builder.Configuration.GetValue<int>("RateLimiting:ApiLimit", 100);

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
            ctx.HttpContext.Response.StatusCode  = StatusCodes.Status429TooManyRequests;
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
    var otelEndpoint = builder.Configuration["OpenTelemetry:Endpoint"];
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
                    opts.Filter = ctx =>
                        !ctx.Request.Path.StartsWithSegments("/health") &&
                        !ctx.Request.Path.StartsWithSegments("/health/ready");
                })
                .AddEntityFrameworkCoreInstrumentation(opts =>
                {
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
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation();

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

    // ── Seed roles + super-admin ──────────────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        await DatabaseSeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
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

        opts.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
        {
            diagCtx.Set("RequestHost", httpCtx.Request.Host.Value);
            diagCtx.Set("UserAgent",   httpCtx.Request.Headers.UserAgent.ToString());
            diagCtx.Set("UserId",      httpCtx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "-");
            diagCtx.Set("TenantId",    httpCtx.User.FindFirst("tenant_id")?.Value ?? "-");
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
    app.MapGet("/health/live", () => Results.Ok(new { Status = "alive", Time = DateTime.UtcNow }))
       .WithTags("Health").AllowAnonymous().ExcludeFromDescription();

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
    }).AllowAnonymous().WithTags("Health");

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
    }).AllowAnonymous().WithTags("Health");

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
