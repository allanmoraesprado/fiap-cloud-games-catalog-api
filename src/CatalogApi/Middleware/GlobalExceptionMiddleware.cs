using System.Net;
using System.Text.Json;
using CatalogApi.Application.Exceptions;
using CatalogApi.Domain.Exceptions;

namespace CatalogApi.Middleware;

public class GlobalExceptionMiddleware
{
    public const int RetryAfterSeconds = 10;

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        var (status, message) = ex switch
        {
            SearchUnavailableException => (HttpStatusCode.ServiceUnavailable, "Search is temporarily unavailable. Please retry shortly."),
            NotFoundException => (HttpStatusCode.NotFound, ex.Message),
            ConflictException => (HttpStatusCode.Conflict, ex.Message),
            ForbiddenException => (HttpStatusCode.Forbidden, ex.Message),
            UnauthorizedException => (HttpStatusCode.Unauthorized, ex.Message),
            DomainException => (HttpStatusCode.BadRequest, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
        };

        if (status == HttpStatusCode.InternalServerError)
            _logger.LogError(ex, "Unhandled exception");
        else
            _logger.LogWarning(ex, "Handled domain exception: {Message}", ex.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)status;
        if (status == HttpStatusCode.ServiceUnavailable)
            context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString();
        var payload = JsonSerializer.Serialize(new
        {
            status = (int)status,
            error = status.ToString(),
            message
        });
        await context.Response.WriteAsync(payload);
    }
}
