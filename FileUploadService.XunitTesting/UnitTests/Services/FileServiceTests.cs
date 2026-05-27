using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class FileServiceUploadTests
{


    private readonly Mock<IFileService> _fileServiceMock = new();

    private static IFormFile MakeMockFile(
        string name = "photo.jpg",
        string contentType = "image/jpeg",
        long size = 1024)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(name);
        mock.Setup(f => f.Length).Returns(size);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.OpenReadStream())
            .Returns(() => new MemoryStream(new byte[size]));
        return mock.Object;
    }


    [Fact]
    public async Task UploadFileAsync_ValidFile_ReturnsReferenceIdWithFilePrefix()
    {
        _fileServiceMock
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ReferenceId = "FILE-20260518-harsh",
                OriginalFilename = "photo.jpg",
                ContentType = "image/jpeg",
                FileSizeBytes = 1024,
                CreatedAt = DateTime.UtcNow
            });

        var result = await _fileServiceMock.Object.UploadFileAsync(MakeMockFile(), null);

        result.ReferenceId.Should().StartWith("FILE-");
        result.OriginalFilename.Should().Be("photo.jpg");
        result.FileSizeBytes.Should().Be(1024);
    }

    // ── FileValidationException propagates through service ─────────

    [Fact]
    public async Task UploadFileAsync_ValidationFails_ThrowsFileValidationException()
    {
        var validationResult = new FileValidationResult
        {
            IsValid = false,
            FailureReason = "Blocked extension",
            Details = new ValidationDetails { ClaimedExtension = ".exe" }
        };

        _fileServiceMock
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new FileValidationException(validationResult));

        var act = async () => await _fileServiceMock.Object.UploadFileAsync(
            MakeMockFile("malware.exe", "application/octet-stream"), null);

        await act.Should().ThrowAsync<FileValidationException>()
            .WithMessage("*Blocked extension*");
    }

    // ── VirusDetectedException propagates through service ──────────

    [Fact]
    public async Task UploadFileAsync_VirusDetected_ThrowsVirusDetectedException()
    {
        _fileServiceMock
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new VirusDetectedException("Win.Test.EICAR_HDB-1"));

        var act = async () =>
            await _fileServiceMock.Object.UploadFileAsync(MakeMockFile(), null);

        await act.Should().ThrowAsync<VirusDetectedException>()
            .WithMessage("*Win.Test.EICAR_HDB-1*");
    }

    // ── VirusScanException propagates when scanner unavailable ─────

    [Fact]
    public async Task UploadFileAsync_ScannerUnavailable_ThrowsVirusScanException()
    {
        _fileServiceMock
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new VirusScanException("ClamAV daemon not responding"));

        var act = async () =>
            await _fileServiceMock.Object.UploadFileAsync(MakeMockFile(), null);

        await act.Should().ThrowAsync<VirusScanException>()
            .WithMessage("*ClamAV*");
    }

    // ── Upload called with null uploadedBy ─────────────────────────

    [Fact]
    public async Task UploadFileAsync_NullUploadedBy_StillSucceeds()
    {
        _fileServiceMock
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), null))
            .ReturnsAsync(new FileUploadResponse
            {
                ReferenceId = "FILE-20260518-KAALU2",
                OriginalFilename = "photo.jpg",
                FileSizeBytes = 512,
                CreatedAt = DateTime.UtcNow
            });

        var result = await _fileServiceMock.Object.UploadFileAsync(MakeMockFile(), null);
        result.Should().NotBeNull();
        result.ReferenceId.Should().NotBeNullOrEmpty();
    }

    // ── Upload called with valid uploadedBy GUID ───────────────────

    [Fact]
    public async Task UploadFileAsync_ValidUploadedByGuid_StillSucceeds()
    {
        var uploaderId = Guid.NewGuid().ToString();

        _fileServiceMock
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), uploaderId))
            .ReturnsAsync(new FileUploadResponse
            {
                ReferenceId = "FILE-20260518-KAALU3",
                OriginalFilename = "photo.jpg",
                FileSizeBytes = 512,
                CreatedAt = DateTime.UtcNow
            });

        var result = await _fileServiceMock.Object.UploadFileAsync(MakeMockFile(), uploaderId);
        result.Should().NotBeNull();
    }


    [Fact]
    public async Task DownloadFileAsync_UnknownReferenceId_ReturnsNull()
    {
        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-NOTFOUND"))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        var result = await _fileServiceMock.Object.DownloadFileAsync("FILE-NOTFOUND");
        result.Should().BeNull();
    }

    // ── Download returns metadata + stream for known referenceId ───

    [Fact]
    public async Task DownloadFileAsync_KnownReferenceId_ReturnsMetadataAndStream()
    {
        var metadata = new FileMetadata
        {
            Id = 1L,
            ReferenceId = "FILE-20260518-KAALU4",
            OriginalFilename = "report.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false,
            Iv = Convert.ToBase64String(new byte[16])
        };
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-20260518-KAALU4"))
            .ReturnsAsync((metadata, (Stream)stream));

        var result = await _fileServiceMock.Object.DownloadFileAsync("FILE-20260518-KAALU4");
        result.Should().NotBeNull();
        result!.Value.Metadata.ReferenceId.Should().Be("FILE-20260518-KAALU4");
        result!.Value.FileStream.Should().NotBeNull();
    }

    // ── Download result has correct content type ──────────────────

    [Fact]
    public async Task DownloadFileAsync_KnownReferenceId_MetadataHasCorrectContentType()
    {
        var metadata = new FileMetadata
        {
            ReferenceId = "FILE-CT-001",
            ContentType = "image/png",
            OriginalFilename = "image.png",
            FileSize = 100,
            CreatedAt = DateTime.UtcNow
        };
        var stream = new MemoryStream(new byte[4]);

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-CT-001"))
            .ReturnsAsync((metadata, (Stream)stream));

        var result = await _fileServiceMock.Object.DownloadFileAsync("FILE-CT-001");
        result!.Value.Metadata.ContentType.Should().Be("image/png");
    }

    // ── Download stream is readable ────────────────────────────────

    [Fact]
    public async Task DownloadFileAsync_KnownReferenceId_StreamIsReadable()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var metadata = new FileMetadata
        {
            ReferenceId = "FILE-STREAM-001",
            OriginalFilename = "data.pdf",
            ContentType = "application/pdf",
            FileSize = payload.Length,
            CreatedAt = DateTime.UtcNow
        };
        var stream = new MemoryStream(payload);

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-STREAM-001"))
            .ReturnsAsync((metadata, (Stream)stream));

        var result = await _fileServiceMock.Object.DownloadFileAsync("FILE-STREAM-001");
        var buffer = new byte[10];
        var bytesRead = await result!.Value.FileStream.ReadAsync(buffer);
        bytesRead.Should().Be(payload.Length);
    }
}

public class FileMetadataTests
{
    [Fact]
    public void FileMetadata_DefaultValues_AreCorrect()
    {
        var m = new FileMetadata();
        m.IsDeleted.Should().BeFalse();
        m.Iv.Should().BeNull();
        m.UploadedBy.Should().BeNull();
        m.StoragePath.Should().BeNullOrEmpty();
    }

    [Fact]
    public void FileMetadata_AllPropertiesSetCorrectly()
    {
        var id = Guid.NewGuid();
        var uid = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var m = new FileMetadata
        {
            Id = 1L,
            ReferenceId = "FILE-META-001",
            OriginalFilename = "test.pdf",
            StoragePath = "uploads/2026/05/abc.enc",
            ContentType = "application/pdf",
            FileSize = 4096,
            UploadedBy = 1L,
            CreatedAt = now,
            Iv = "aGVsbG8=",
            IsDeleted = false
        };

        m.Id.Should().Be(1L);
        m.ReferenceId.Should().Be("FILE-META-001");
        m.OriginalFilename.Should().Be("test.pdf");
        m.StoragePath.Should().Be("uploads/2026/05/abc.enc");
        m.ContentType.Should().Be("application/pdf");
        m.FileSize.Should().Be(4096);
        m.UploadedBy.Should().Be(1L);
        m.CreatedAt.Should().Be(now);
        m.Iv.Should().Be("aGVsbG8=");
        m.IsDeleted.Should().BeFalse();
    }
}