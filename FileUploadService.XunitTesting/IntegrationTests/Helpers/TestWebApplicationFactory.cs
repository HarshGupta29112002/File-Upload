using FileUploadService.Application.Interfaces;
using FileUploadService.Application.DTOs;
using Microsoft.AspNetCore.Http;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace FileUploadService.XunitTesting.IntegrationTests.Helpers;

/// <summary>
/// Spins up the real ASP.NET pipeline but replaces all external dependencies
/// (PostgreSQL via Dapper, ClamAV, disk I/O) with Moq mocks.
/// Mirrors exactly the pattern used in IplookupService.XunitTesting.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
	// ── Exposed mocks ──────────────────────────────────────────────
	public Mock<IFileService> FileService { get; } = new();

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.ConfigureServices(services =>
		{
			// Remove all background hosted services (defensive)
			services.RemoveAll<IHostedService>();

			// Remove real IFileService registered in Program.cs
			services.RemoveAll<IFileService>();
			services.RemoveAll<IClamClientFactory>();

			// Register mocks
			services.AddSingleton(_ => FileService.Object);
			services.AddSingleton<IClamClientFactory>(_ =>
			{
				var factory = new Mock<IClamClientFactory>();
				var client = new Mock<IVirusScanClient>();
				client.Setup(c => c.PingAsync()).ReturnsAsync(true);
				factory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<int>())).Returns(client.Object);
				return factory.Object;
			});
		});
	}

	// ─────────────────────────────────────────────────────────────
	// Helper: reset all mocks between tests
	// ─────────────────────────────────────────────────────────────
	public void ResetMocks()
	{
		FileService.Reset();
	}

	// ─────────────────────────────────────────────────────────────
	// Helper: configure a happy-path upload response
	// ─────────────────────────────────────────────────────────────
	public void SetupHappyUpload(string referenceId = "FILE-20260516-harsh")
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

	// ─────────────────────────────────────────────────────────────
	// Helper: configure a happy-path download response
	// ─────────────────────────────────────────────────────────────
	public void SetupHappyDownload(string referenceId = "FILE-20260516-harsh")
	{
		ResetMocks();

		var metadata = new FileMetadata
		{
			Id = 1L,
			ReferenceId = referenceId,
			OriginalFilename = "test-file.pdf",
			StoragePath = "uploads/2026/05/somefile.enc",
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
	}
}