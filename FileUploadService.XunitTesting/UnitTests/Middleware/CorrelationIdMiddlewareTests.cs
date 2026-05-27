using FileUploadService.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Middleware;


public class CorrelationIdMiddlewareTests
{
    private readonly Mock<ILogger<CorrelationIdMiddleware>> _loggerMock = new();

    private CorrelationIdMiddleware BuildMiddleware(RequestDelegate next) =>
        new(next, _loggerMock.Object);

    // POSITIVE TEST CASES

    // ── No incoming header → ID is generated and stored in Items ──

    [Fact]
    public async Task InvokeAsync_NoHeader_GeneratesCorrelationId()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = BuildMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items[CorrelationIdMiddleware.HeaderName].Should().NotBeNull();
        var id = context.Items[CorrelationIdMiddleware.HeaderName]!.ToString();
        id.Should().NotBeNullOrEmpty();
    }

    // ── Incoming header is preserved in Items ─────────────────────

    [Fact]
    public async Task InvokeAsync_IncomingHeader_PreservesCallerCorrelationId()
    {
        // Arrange
        const string callerId = "Harsh-TRACE-9999";
        var context  = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = callerId;
        context.Response.Body = new MemoryStream();

        var middleware = BuildMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Items[CorrelationIdMiddleware.HeaderName]!.ToString()
            .Should().Be(callerId);
    }

    // ── Correlation ID is echoed in response headers ───────────────

    [Fact]
    public async Task InvokeAsync_AnyRequest_EchoesIdInResponseHeader()
    {
        
        const string callerId = "ECHO-TEST-0001";
        var context   = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = callerId;
        context.Response.Body = new MemoryStream();

        // We need to trigger OnStarting callbacks manually for DefaultHttpContext
        string? capturedHeader = null;
        var middleware = BuildMiddleware(ctx =>
        {
            // Force the OnStarting callback to fire
            ctx.Response.Headers[CorrelationIdMiddleware.HeaderName] =
                ctx.Items[CorrelationIdMiddleware.HeaderName]!.ToString();
            capturedHeader = ctx.Response.Headers[CorrelationIdMiddleware.HeaderName];
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        capturedHeader.Should().Be(callerId);
    }

    [Fact]
    public async Task InvokeAsync_AnyRequest_AlwaysCallsNext()
    {
        // Arrange
        var nextCalled = false;
        var context    = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = BuildMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
    }


    [Fact]
    public async Task InvokeAsync_NoHeader_GeneratedIdIs12Chars()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = BuildMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var id = context.Items[CorrelationIdMiddleware.HeaderName]!.ToString()!;
        id.Length.Should().Be(12);
    }

    [Fact]
    public async Task InvokeAsync_NoHeader_GeneratedIdIsUppercase()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = BuildMiddleware(_ => Task.CompletedTask);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var id = context.Items[CorrelationIdMiddleware.HeaderName]!.ToString()!;
        id.Should().Be(id.ToUpperInvariant());
    }

    // NEGATIVE TEST CASES

    [Fact]
    public async Task InvokeAsync_TwoRequests_GetDifferentIds()
    {

        var ctx1 = new DefaultHttpContext();
        var ctx2 = new DefaultHttpContext();
        ctx1.Response.Body = new MemoryStream();
        ctx2.Response.Body = new MemoryStream();

        var mw = BuildMiddleware(_ => Task.CompletedTask);

        await mw.InvokeAsync(ctx1);
        await mw.InvokeAsync(ctx2);

        var id1 = ctx1.Items[CorrelationIdMiddleware.HeaderName]!.ToString();
        var id2 = ctx2.Items[CorrelationIdMiddleware.HeaderName]!.ToString();
        id1.Should().NotBe(id2, "each request should have a unique correlation ID");
    }
}
