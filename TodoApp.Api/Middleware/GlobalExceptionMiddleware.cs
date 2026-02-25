using System.Net;
using System.Text.Json;
using TodoApp.Domain.Exceptions;

namespace TodoApp.Api.Middleware;

public class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteErrorResponse(context, ex);
        }
    }

    private static async Task WriteErrorResponse(HttpContext context, Exception ex)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message, errors) = ex switch
        {
            NotFoundException           => (HttpStatusCode.NotFound,             ex.Message, (IEnumerable<string>?)null),
            ValidationException ve      => (HttpStatusCode.BadRequest,           ex.Message, ve.Errors),
            ConflictException           => (HttpStatusCode.Conflict,             ex.Message, null),
            ForbiddenException          => (HttpStatusCode.Forbidden,            ex.Message, null),
            AccountLockedException      => ((HttpStatusCode)423,                 ex.Message, null),
            TenantInactiveException     => (HttpStatusCode.Forbidden,            ex.Message, null),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized,         ex.Message, null),
            ArgumentException           => (HttpStatusCode.BadRequest,           ex.Message, null),
            OperationCanceledException  => (HttpStatusCode.ServiceUnavailable,  "Request cancelled.", null),
            _                           => (HttpStatusCode.InternalServerError,  "An unexpected error occurred.", null)
        };

        context.Response.StatusCode = (int)statusCode;

        object payload = errors is not null
            ? new { error = message, errors, traceId = context.TraceIdentifier }
            : new { error = message, traceId = context.TraceIdentifier };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, _jsonOpts));
    }
}
