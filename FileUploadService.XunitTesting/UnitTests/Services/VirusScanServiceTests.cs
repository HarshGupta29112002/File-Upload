using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net.Sockets;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

// =========================================================
// VirusScanResult factory method tests
// =========================================================
public class VirusScanResultTests
{
    [Fact]
    public void Clean_ReturnsIsCleanTrue()
    {
        var result = VirusScanResult.Clean();
        result.IsClean.Should().BeTrue();
        result.ScannerUnavailable.Should().BeFalse();
        result.ThreatName.Should().BeNull();
    }

    [Fact]
    public void Infected_ReturnsIsCleanFalseWithThreatName()
    {
        var result = VirusScanResult.Infected("Win.Test.EICAR_HDB-1");
        result.IsClean.Should().BeFalse();
        result.ThreatName.Should().Be("Win.Test.EICAR_HDB-1");
        result.ScannerUnavailable.Should().BeFalse();
    }

    [Fact]
    public void Unavailable_ReturnsScannerUnavailableTrue()
    {
        var result = VirusScanResult.Unavailable();
        result.ScannerUnavailable.Should().BeTrue();
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public void Infected_EmptyThreatName_StillReturnsInfected()
    {
        var result = VirusScanResult.Infected("");
        result.IsClean.Should().BeFalse();
        result.ThreatName.Should().BeEmpty();
    }
}

// =========================================================
// VirusScanService — all branches via mocked IVirusScanClient
// =========================================================
public class VirusScanServiceTests
{
    private readonly Mock<ILogger<VirusScanService>> _loggerMock = new();
    private readonly Mock<IClamClientFactory> _factoryMock = new();
    private readonly Mock<IVirusScanClient> _clientMock = new();

    private VirusScanService BuildService()
    {
        var settings = Options.Create(new ClamAvSettings
        {
            Host = "127.0.0.1",
            Port = 3310,
            TimeoutSeconds = 5
        });
        _factoryMock
            .Setup(f => f.Create(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(_clientMock.Object);
        return new VirusScanService(settings, _loggerMock.Object, _factoryMock.Object);
    }

    private static IFormFile MakeMockFile(string name = "test.pdf", long size = 512)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(name);
        mock.Setup(f => f.Length).Returns(size);
        mock.Setup(f => f.ContentType).Returns("application/pdf");
        mock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[size]));
        return mock.Object;
    }

    // ── BRANCH 1: Ping fails → Unavailable ────────────────────────

    [Fact]
    public async Task ScanAsync_PingFails_ReturnsUnavailable()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(false);
        var result = await BuildService().ScanAsync(MakeMockFile());
        result.ScannerUnavailable.Should().BeTrue();
        result.IsClean.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_PingFails_LogsError()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(false);
        await BuildService().ScanAsync(MakeMockFile());
        _loggerMock.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── BRANCH 2: Clean file ───────────────────────────────────────

    [Fact]
    public async Task ScanAsync_CleanFile_ReturnsIsCleanTrue()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Clean());
        var result = await BuildService().ScanAsync(MakeMockFile());
        result.IsClean.Should().BeTrue();
        result.ScannerUnavailable.Should().BeFalse();
        result.ThreatName.Should().BeNull();
    }

    [Fact]
    public async Task ScanAsync_CleanFile_LogsInformation()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Clean());
        await BuildService().ScanAsync(MakeMockFile());
        _loggerMock.Verify(l => l.Log(
            LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeast(2)); // "Starting scan" + "Virus scan passed"
    }

    // ── BRANCH 3: VirusDetected ────────────────────────────────────

    [Fact]
    public async Task ScanAsync_VirusDetected_ReturnsInfectedWithThreatName()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Infected("Win.Test.EICAR_HDB-1"));
        var result = await BuildService().ScanAsync(MakeMockFile());
        result.IsClean.Should().BeFalse();
        result.ThreatName.Should().Be("Win.Test.EICAR_HDB-1");
        result.ScannerUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_VirusDetected_LogsWarning()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Infected("Trojan.Generic"));
        await BuildService().ScanAsync(MakeMockFile());
        _loggerMock.Verify(l => l.Log(
            LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_VirusDetected_NullThreat_FallsBackToUnknown()
    {
        // VirusScanResult.Infected(null) → ThreatName is null → service returns "Unknown threat"
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(new VirusScanResult { IsClean = false, ThreatName = null, ScannerUnavailable = false });
        var result = await BuildService().ScanAsync(MakeMockFile());
        result.IsClean.Should().BeFalse();
        result.ThreatName.Should().Be("Unknown threat");
    }

    // ── BRANCH 4: Scanner error result ────────────────────────────

    [Fact]
    public async Task ScanAsync_ScannerUnavailableResult_ReturnsUnavailable()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Unavailable());
        var result = await BuildService().ScanAsync(MakeMockFile());
        result.ScannerUnavailable.Should().BeTrue();
        result.IsClean.Should().BeFalse();
    }

    // ── BRANCH 5: Exception → catch → Unavailable ─────────────────

    [Fact]
    public async Task ScanAsync_ExceptionOnPing_ReturnsUnavailable()
    {
        _clientMock.Setup(c => c.PingAsync())
            .ThrowsAsync(new IOException("Connection refused"));
        var result = await BuildService().ScanAsync(MakeMockFile());
        result.ScannerUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task ScanAsync_ExceptionOnPing_NeverThrows()
    {
        _clientMock.Setup(c => c.PingAsync())
            .ThrowsAsync(new SocketException());
        var act = async () => await BuildService().ScanAsync(MakeMockFile());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ScanAsync_ExceptionOnPing_LogsError()
    {
        _clientMock.Setup(c => c.PingAsync())
            .ThrowsAsync(new IOException("timeout"));
        await BuildService().ScanAsync(MakeMockFile());
        _loggerMock.Verify(l => l.Log(
            LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ── Factory called with correct host/port ──────────────────────

    [Fact]
    public async Task ScanAsync_CallsFactoryWithConfiguredHostAndPort()
    {
        _clientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Clean());
        await BuildService().ScanAsync(MakeMockFile());
        _factoryMock.Verify(f => f.Create("127.0.0.1", 3310), Times.Once);
    }
}