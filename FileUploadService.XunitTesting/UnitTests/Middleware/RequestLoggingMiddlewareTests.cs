using FileUploadService.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Middleware;


public class RequestLoggingMiddlewareTests
{
    private readonly Mock<ILogger<RequestLoggingMiddleware>> _loggerMock = new();

    private static IConfiguration BuildConfig(string? logPath = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["LogSettings:BasePath"] = logPath ?? Path.Combine(Path.GetTempPath(), "TestLogs_" + Guid.NewGuid().ToString("N"))
        };
        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private RequestLoggingMiddleware BuildMiddleware(RequestDelegate next, string? logPath = null)
        => new(next, BuildConfig(logPath));

    private static DefaultHttpContext MakeContext(
        string method = "GET",
        string path = "/api/files/FILE-001",
        string body = "",
        int statusCode = 200)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        ctx.Response.StatusCode = statusCode;
        ctx.Items[CorrelationIdMiddleware.HeaderName] = "TEST-CORR-001";

        if (!string.IsNullOrEmpty(body))
        {
            var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
            ctx.Request.Body = new MemoryStream(bodyBytes);
            ctx.Request.ContentLength = bodyBytes.Length;
        }
        else
        {
            ctx.Request.Body = new MemoryStream();
        }

        return ctx;
    }

    // ── Normal GET request → next is called ───────────────────────

    [Fact]
    public async Task InvokeAsync_NormalRequest_CallsNext()
    {
        var nextCalled = false;
        var ctx = MakeContext();
        var mw = BuildMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await mw.InvokeAsync(ctx);

        nextCalled.Should().BeTrue();
    }

    // ── Creates log directories ────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NormalRequest_CreatesLogDirectories()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "LogDirTest_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext();
        var mw = BuildMiddleware(_ => Task.CompletedTask, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var expected = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"));
        Directory.Exists(Path.Combine(expected, "request")).Should().BeTrue();
        Directory.Exists(Path.Combine(expected, "response")).Should().BeTrue();
        Directory.Exists(Path.Combine(expected, "application")).Should().BeTrue();

        Directory.Delete(tempBase, true);
    }

    // ── Writes request log file ────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NormalRequest_WritesRequestLogFile()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "ReqLog_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext("GET", "/api/files/TEST");
        var mw = BuildMiddleware(_ => Task.CompletedTask, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var requestPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "request");
        var files = Directory.GetFiles(requestPath);
        files.Should().HaveCount(1);
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("GET");
        content.Should().Contain("/api/files/TEST");

        Directory.Delete(tempBase, true);
    }

    // ── Writes response log file ───────────────────────────────────

    [Fact]
    public async Task InvokeAsync_NormalRequest_WritesResponseLogFile()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "ResLog_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext();
        var mw = BuildMiddleware(_ => Task.CompletedTask, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var responsePath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "response");
        var files = Directory.GetFiles(responsePath);
        files.Should().HaveCount(1);

        Directory.Delete(tempBase, true);
    }

    // ── 400 response → writes error log ───────────────────────────

    [Fact]
    public async Task InvokeAsync_400Response_WritesErrorLog()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "ErrLog_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext();
        var mw = BuildMiddleware(c => { c.Response.StatusCode = 400; return Task.CompletedTask; }, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var errorPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "error");
        var files = Directory.GetFiles(errorPath);
        files.Should().HaveCount(1);
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("400");

        Directory.Delete(tempBase, true);
    }

    // ── 500 response → writes error log ───────────────────────────

    [Fact]
    public async Task InvokeAsync_500Response_WritesErrorLog()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "ErrLog500_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext();
        var mw = BuildMiddleware(c => { c.Response.StatusCode = 500; return Task.CompletedTask; }, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var errorPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "error");
        Directory.GetFiles(errorPath).Should().HaveCount(1);

        Directory.Delete(tempBase, true);
    }

    // ── 200 response → no error log created ───────────────────────

    [Fact]
    public async Task InvokeAsync_200Response_NoErrorLogCreated()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "NoErrLog_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext();
        var mw = BuildMiddleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; }, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var errorPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "error");
        if (Directory.Exists(errorPath))
            Directory.GetFiles(errorPath).Should().BeEmpty();

        Directory.Delete(tempBase, true);
    }

    // ── Request with body → body captured in log ──────────────────

    [Fact]
    public async Task InvokeAsync_RequestWithBody_CapturesBodyInLog()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "BodyLog_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext("POST", "/api/files/upload", "{\"key\":\"value\"}");
        var mw = BuildMiddleware(_ => Task.CompletedTask, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var requestPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "request");
        var files = Directory.GetFiles(requestPath);
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("{\"key\":\"value\"}");

        Directory.Delete(tempBase, true);
    }

    // ── Exception rethrown → error log written ─────────────────────

    [Fact]
    public async Task InvokeAsync_NextThrows_WritesErrorLogAndRethrows()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "ExLog_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext();
        var mw = BuildMiddleware(_ => throw new InvalidOperationException("test error"), tempBase);

        var act = async () => await mw.InvokeAsync(ctx);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var now = DateTime.Now;
        var errorPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "error");
        var files = Directory.GetFiles(errorPath);
        files.Should().HaveCount(1);
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("test error");

        Directory.Delete(tempBase, true);
    }

    // ── Application log always written (success or failure) ────────

    [Fact]
    public async Task InvokeAsync_NormalRequest_WritesApplicationLog()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "AppLog_" + Guid.NewGuid().ToString("N"));
        var ctx = MakeContext();
        var mw = BuildMiddleware(_ => Task.CompletedTask, tempBase);

        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var appPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "application");
        var files = Directory.GetFiles(appPath);
        files.Should().HaveCount(1);
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("Request started");
        content.Should().Contain("Request completed");

        Directory.Delete(tempBase, true);
    }

    // ── No correlationId in Items → uses N/A ──────────────────────

    [Fact]
    public async Task InvokeAsync_NoCorrelationId_UsesNaFallback()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), "NoCorrLog_" + Guid.NewGuid().ToString("N"));
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.Path = "/api/test";
        ctx.Request.Body = new MemoryStream();
        ctx.Response.Body = new MemoryStream();
        // deliberately NOT setting Items[CorrelationIdMiddleware.HeaderName]

        var mw = BuildMiddleware(_ => Task.CompletedTask, tempBase);
        await mw.InvokeAsync(ctx);

        var now = DateTime.Now;
        var requestPath = Path.Combine(tempBase, now.Year.ToString(), now.ToString("MM"), now.ToString("dd"), "request");
        var files = Directory.GetFiles(requestPath);
        var content = await File.ReadAllTextAsync(files[0]);
        content.Should().Contain("N/A");

        Directory.Delete(tempBase, true);
    }
}