using FileUploadService.Application.DTOs;
using FileUploadService.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System.Text.Json;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Middleware;

public class GlobalExceptionMiddlewareTests
{
    private readonly Mock<ILogger<GlobalExceptionMiddleware>> _loggerMock = new();

    private GlobalExceptionMiddleware BuildMiddleware(RequestDelegate next) =>
        new(next, _loggerMock.Object);

    // POSITIVE TEST CASES


    [Fact]
    public async Task InvokeAsync_NoException_CallsNext()
    {
        
        var nextCalled = false;
        var context    = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Items[CorrelationIdMiddleware.HeaderName] = "Harsh-CORR-001";

        var middleware = BuildMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        
        await middleware.InvokeAsync(context);

        
        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_NoException_ResponseBodyIsEmpty()
    {
      
        var body    = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;

        var middleware = BuildMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

    
        body.Length.Should().Be(0);
    }

    
    // NEGATIVE TEST CASES — Generic Exception

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500()
    {
    
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Items[CorrelationIdMiddleware.HeaderName] = "Harsh-CORR-002";

        var middleware = BuildMiddleware(_ => throw new Exception("something broke"));

      
        await middleware.InvokeAsync(context);

     
        context.Response.StatusCode.Should().Be(500);
    }


    [Fact]
    public async Task InvokeAsync_GenericException_ContentTypeIsJson()
    {
       
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = BuildMiddleware(_ => throw new Exception("error"));

       
        await middleware.InvokeAsync(context);

      
        context.Response.ContentType.Should().Be("application/json");
    }


    [Fact]
    public async Task InvokeAsync_GenericException_ResponseBodyHasSuccessFalse()
    {
        var body    = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;

        var middleware = BuildMiddleware(_ => throw new Exception("test error"));

        await middleware.InvokeAsync(context);

        body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(body).ReadToEndAsync();
        var doc  = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    // NEGATIVE TEST CASES — FileValidationException


    [Fact]
    public async Task InvokeAsync_FileValidationException_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Items[CorrelationIdMiddleware.HeaderName] = "Harsh-CORR-003";

        var validationResult = new FileValidationResult
        {
            IsValid       = false,
            FailureReason = "File type not allowed",
            Details       = new ValidationDetails
            {
                ClaimedExtension = ".exe",
                ClaimedMimeType  = "application/octet-stream",
                DetectedFileType = "Windows PE executable",
                FileSizeBytes    = 4096
            }
        };

        var middleware = BuildMiddleware(_ => throw new FileValidationException(validationResult));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }


    [Fact]
    public async Task InvokeAsync_FileValidationException_ResponseContainsFailureReason()
    {
        var body    = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;

        var validationResult = new FileValidationResult
        {
            IsValid       = false,
            FailureReason = "File type not allowed",
            Details       = new ValidationDetails { ClaimedExtension = ".exe" }
        };

        var middleware = BuildMiddleware(_ => throw new FileValidationException(validationResult));

        await middleware.InvokeAsync(context);

        body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(body).ReadToEndAsync();
        text.Should().Contain("File type not allowed");
    }

    // NEGATIVE TEST CASES — VirusDetectedException


    [Fact]
    public async Task InvokeAsync_VirusDetectedException_Returns422()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = BuildMiddleware(_ =>
            throw new VirusDetectedException("Win.Test.EICAR_HDB-1"));

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(422);
    }


    [Fact]
    public async Task InvokeAsync_VirusDetectedException_ResponseContainsVirusName()
    {
     
        var body    = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Response.Body = body;

        var middleware = BuildMiddleware(_ =>
            throw new VirusDetectedException("Trojan.Generic.12345"));

        
        await middleware.InvokeAsync(context);

        
        body.Seek(0, SeekOrigin.Begin);
        var text = await new StreamReader(body).ReadToEndAsync();
        text.Should().Contain("Trojan.Generic.12345");
    }

    // NEGATIVE TEST CASES — VirusScanException (scanner unavailable)


    [Fact]
    public async Task InvokeAsync_VirusScanException_Returns503()
    {
        
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var middleware = BuildMiddleware(_ =>
            throw new VirusScanException("ClamAV daemon not responding"));

        
        await middleware.InvokeAsync(context);

        
        context.Response.StatusCode.Should().Be(503);
    }
}
