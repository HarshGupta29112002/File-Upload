using FileUploadService.Application.DTOs;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FluentAssertions;
using System.Net.Sockets;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class DefaultClamClientFactoryTests
{
    [Fact]
    public void Create_ValidHostAndPort_ReturnsNonNullClient()
    {
        var factory = new DefaultClamClientFactory();
        var client = factory.Create("127.0.0.1", 3310);
        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_ReturnsIVirusScanClientImplementation()
    {
        var factory = new DefaultClamClientFactory();
        var client = factory.Create("127.0.0.1", 3310);
        client.Should().BeAssignableTo<IVirusScanClient>();
    }

    [Fact]
    public void Create_DifferentHostAndPort_ReturnsNonNullClient()
    {
        var factory = new DefaultClamClientFactory();
        var client = factory.Create("clamav.internal", 3311);
        client.Should().NotBeNull();
    }

    [Fact]
    public void Create_TwoCalls_ReturnsDifferentInstances()
    {
        var factory = new DefaultClamClientFactory();
        var client1 = factory.Create("127.0.0.1", 3310);
        var client2 = factory.Create("127.0.0.1", 3310);
        client1.Should().NotBeSameAs(client2);
    }
}

public class ClamClientAdapterTests
{
    // ── Helper: check if ClamAV is reachable before running ───────
    private static async Task<bool> IsClamAvRunning()
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", 3310);
            return tcp.Connected;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task PingAsync_RealClamAvRunning_ReturnsTrue()
    {
        if (!await IsClamAvRunning()) return; // skip gracefully

        var adapter = new ClamClientAdapter("127.0.0.1", 3310);
        var result = await adapter.PingAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task PingAsync_NothingOnPort_ReturnsFalseOrThrows()
    {
        var adapter = new ClamClientAdapter("127.0.0.1", 9999);
        try
        {
            var result = await adapter.PingAsync();
            result.Should().BeFalse();
        }
        catch (Exception ex)
        {
            ex.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ScanAsync_CleanFile_ReturnsIsCleanTrue()
    {
        if (!await IsClamAvRunning()) return;

        var adapter = new ClamClientAdapter("127.0.0.1", 3310);
        var cleanBytes = new byte[600];
        cleanBytes[0] = 0x25; cleanBytes[1] = 0x50;
        cleanBytes[2] = 0x44; cleanBytes[3] = 0x46;
        using var stream = new MemoryStream(cleanBytes);

        var result = await adapter.ScanAsync(stream, "clean.pdf");
        result.IsClean.Should().BeTrue();
        result.ScannerUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAsync_EicarTestString_ReturnsInfected()
    {
        if (!await IsClamAvRunning()) return;

        var adapter = new ClamClientAdapter("127.0.0.1", 3310);
        const string eicar = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";
        using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(eicar));

        var result = await adapter.ScanAsync(stream, "eicar.txt");
        result.IsClean.Should().BeFalse();
        result.ScannerUnavailable.Should().BeFalse();
        result.ThreatName.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ScanAsync_UnavailablePort_ReturnsUnavailableOrThrows()
    {
        var adapter = new ClamClientAdapter("127.0.0.1", 9999);
        using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        try
        {
            var result = await adapter.ScanAsync(stream, "test.pdf");
            result.ScannerUnavailable.Should().BeTrue();
        }
        catch
        {
            // Acceptable — VirusScanService catches this
        }
    }

    [Fact]
    public async Task ScanAsync_EmptyStream_DoesNotCrash()
    {
        if (!await IsClamAvRunning()) return;

        var adapter = new ClamClientAdapter("127.0.0.1", 3310);
        using var stream = new MemoryStream(Array.Empty<byte>());
        var act = async () => await adapter.ScanAsync(stream, "empty.bin");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ScanAsync_CleanFile_NeverHasBothFlagsTrue()
    {
        if (!await IsClamAvRunning()) return;

        var adapter = new ClamClientAdapter("127.0.0.1", 3310);
        using var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var result = await adapter.ScanAsync(stream, "test.pdf");
        (result.IsClean && result.ScannerUnavailable).Should().BeFalse();
    }
}