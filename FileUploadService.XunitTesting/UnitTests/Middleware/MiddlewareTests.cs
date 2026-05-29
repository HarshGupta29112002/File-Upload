using FileUploadService.Application.DTOs;
using FileUploadService.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Middleware;

// =========================================================
// CorrelationIdMiddlewareTests
// =========================================================
public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoIncomingHeader_GeneratesCorrelationId()
    {
        var context = MakeContext();
        var sut = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Items[CorrelationIdMiddleware.HeaderName].Should().NotBeNull();
    }

    [Fact]
    public async Task InvokeAsync_ExistingHeader_UsesProvidedCorrelationId()
    {
        var context = MakeContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "MY-CORRELATION-ID";

        var sut = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Items[CorrelationIdMiddleware.HeaderName].Should().Be("MY-CORRELATION-ID");
    }

    [Fact]
    public async Task InvokeAsync_EchoesCorrelationIdInResponseHeader()
    {
        var context = MakeContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "ECHO-ME-123";

        var sut = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        await sut.InvokeAsync(context);

        // Trigger OnStarting callbacks
        await context.Response.StartAsync();
        context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString()
            .Should().Be("ECHO-ME-123");
    }

    [Fact]
    public async Task InvokeAsync_HeaderName_IsXCorrelationID()
        => CorrelationIdMiddleware.HeaderName.Should().Be("X-Correlation-ID");

    private static DefaultHttpContext MakeContext() => new();
}

// =========================================================
// GlobalExceptionMiddlewareTests
// =========================================================
public class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoException_PassesThrough()
    {
        var context = MakeContext();
        var reached = false;

        var sut = new GlobalExceptionMiddleware(
            _ => { reached = true; return Task.CompletedTask; },
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await sut.InvokeAsync(context);

        reached.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_FileValidationException_Returns400()
    {
        var context = MakeContext();
        var result = new FileValidationResult
        {
            IsValid = false,
            FailureReason = "Extension not allowed",
            Details = new ValidationDetails { ClaimedExtension = ".exe" }
        };

        var sut = new GlobalExceptionMiddleware(
            _ => throw new FileValidationException(result),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InvokeAsync_VirusDetectedException_Returns422()
    {
        var context = MakeContext();

        var sut = new GlobalExceptionMiddleware(
            _ => throw new VirusDetectedException("Win.Test.EICAR_HDB-1"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task InvokeAsync_VirusScanException_Returns503()
    {
        var context = MakeContext();

        var sut = new GlobalExceptionMiddleware(
            _ => throw new VirusScanException("ClamAV not responding"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_Returns500()
    {
        var context = MakeContext();

        var sut = new GlobalExceptionMiddleware(
            _ => throw new Exception("something went wrong"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.StatusCode.Should().Be((int)HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledException_ResponseIsJson()
    {
        var context = MakeContext();

        var sut = new GlobalExceptionMiddleware(
            _ => throw new Exception("boom"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.ContentType.Should().Contain("application/json");
    }

    [Fact]
    public async Task InvokeAsync_FileValidationException_ResponseContainsSuccessFalse()
    {
        var context = MakeContext();
        var result = new FileValidationResult
        {
            IsValid = false,
            FailureReason = "File type not allowed",
            Details = new ValidationDetails()
        };

        var sut = new GlobalExceptionMiddleware(
            _ => throw new FileValidationException(result),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await sut.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    private static DefaultHttpContext MakeContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        // Set a fake correlation ID so middleware can read it
        context.Items[CorrelationIdMiddleware.HeaderName] = "TEST-CORR-ID";
        return context;
    }
}