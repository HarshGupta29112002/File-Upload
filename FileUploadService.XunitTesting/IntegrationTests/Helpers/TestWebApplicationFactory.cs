using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;

namespace FileUploadService.XunitTesting.IntegrationTests.Helpers;

/// <summary>
/// Spins up the real ASP.NET pipeline but replaces all external dependencies
/// (PostgreSQL, ClamAV, disk I/O) with Moq mocks.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    public Mock<IFileService> FileService { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<IFileService>();
            services.RemoveAll<IClamClientFactory>();

            services.AddSingleton(_ => FileService.Object);

            services.AddSingleton<IClamClientFactory>(_ =>
            {
                var factory = new Mock<IClamClientFactory>();
                var client = new Mock<IVirusScanClient>();
                client.Setup(c => c.PingAsync()).ReturnsAsync(true);
                factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<int>()))
                       .Returns(client.Object);
                return factory.Object;
            });
        });
    }

    public void ResetMocks() => FileService.Reset();

    // ── Happy-path upload ─────────────────────────────────────────
    public void SetupHappyUpload(string referenceId = "FILE-20260516-ABCDEF")
    {
        ResetMocks();
        FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ReferenceId = referenceId,
                OriginalFilename = "test-file.pdf",
                ContentType = "application/pdf",
                FileSizeBytes = 1024,
                CreatedAt = DateTime.UtcNow
            });
    }

    // ── Happy-path download ───────────────────────────────────────
    public void SetupHappyDownload(string referenceId = "FILE-20260516-ABCDEF")
    {
        ResetMocks();

        var metadata = new FileMetadata
        {
            Id = 1L,
            ReferenceId = referenceId,
            OriginalFilename = "test-file.pdf",
            StoragePath = "uploads/2026/05/test.enc",
            ContentType = "application/pdf",
            FileSize = 1024,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
            Iv = Convert.ToBase64String(new byte[16])
        };

        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        FileService
            .Setup(s => s.DownloadFileAsync(referenceId))
            .ReturnsAsync((metadata, (Stream)stream));

        // Also wire metadata endpoint (uses GetFileMetadataAsync)
        FileService
            .Setup(s => s.GetFileMetadataAsync(referenceId))
            .ReturnsAsync(metadata);
    }
}