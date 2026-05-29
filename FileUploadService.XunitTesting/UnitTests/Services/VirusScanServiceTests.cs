using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class VirusScanServiceTests
{
    private readonly Mock<IClamClientFactory> _factory = new();
    private readonly Mock<IVirusScanClient> _client = new();
    private readonly VirusScanService _sut;

    public VirusScanServiceTests()
    {
        _factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<int>()))
                .Returns(_client.Object);

        _sut = new VirusScanService(
            Options.Create(new ClamAvSettings()),
            NullLogger<VirusScanService>.Instance,
            _factory.Object);
    }

    [Fact]
    public async Task ScanAsync_CleanFile_ReturnsCleanResult()
    {
        _client.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _client.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
               .ReturnsAsync(VirusScanResult.Clean());

        var result = await _sut.ScanAsync(MakeFile("clean.pdf"));
        result.IsClean.Should().BeTrue();
        result.ScannerUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_InfectedFile_ReturnsInfectedResult()
    {
        _client.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _client.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
               .ReturnsAsync(VirusScanResult.Infected("Win.Test.EICAR_HDB-1"));

        var result = await _sut.ScanAsync(MakeFile("virus.pdf"));
        result.IsClean.Should().BeFalse();
        result.ThreatName.Should().Be("Win.Test.EICAR_HDB-1");
    }

    [Fact]
    public async Task ScanAsync_PingFails_ReturnsUnavailable()
    {
        _client.Setup(c => c.PingAsync()).ReturnsAsync(false);

        var result = await _sut.ScanAsync(MakeFile("file.pdf"));
        result.ScannerUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task ScanAsync_ScanThrowsException_ReturnsUnavailable()
    {
        _client.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _client.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
               .ThrowsAsync(new Exception("ClamAV connection refused"));

        var result = await _sut.ScanAsync(MakeFile("file.pdf"));
        result.ScannerUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task ScanAsync_ScannerReturnsUnavailable_ReturnsUnavailable()
    {
        _client.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _client.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
               .ReturnsAsync(VirusScanResult.Unavailable());

        var result = await _sut.ScanAsync(MakeFile("file.pdf"));
        result.ScannerUnavailable.Should().BeTrue();
    }

    private static IFormFile MakeFile(string name)
    {
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        return new FormFile(stream, 0, stream.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
    }
}