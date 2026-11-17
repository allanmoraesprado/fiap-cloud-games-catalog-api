using System.Text.Json;
using CatalogApi.Application.Exceptions;
using CatalogApi.Domain.Exceptions;
using CatalogApi.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CatalogApi.Tests;

public class GlobalExceptionMiddlewareTests
{
    private static async Task<(HttpContext Context, JsonElement Body)> Run(Exception ex)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(_ => throw ex, NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, body.RootElement);
    }

    [Fact]
    public async Task Search_unavailable_becomes_503_with_retry_after()
    {
        var (context, body) = await Run(new SearchUnavailableException("connection refused"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        context.Response.Headers.RetryAfter.ToString().Should().Be(GlobalExceptionMiddleware.RetryAfterSeconds.ToString());
        body.GetProperty("status").GetInt32().Should().Be(503);
        // Internal details (host, exception text) are not leaked to the client.
        body.GetProperty("message").GetString().Should().NotContain("connection refused");
    }

    [Fact]
    public async Task Invalid_search_input_becomes_400()
    {
        var (context, body) = await Run(new DomainException("Query parameter 'q' must have between 2 and 100 characters."));

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        body.GetProperty("message").GetString().Should().Contain("'q'");
    }
}
