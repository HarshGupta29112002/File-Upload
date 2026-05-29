using FileUploadService.Application.Configurations;
using FileUploadService.Application.Implementation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class FileValidationServiceTests
{
    private readonly FileValidationService _sut;

    public FileValidationServiceTests()
    {
        var settings = Options.Create(new FileStorageSettings
        {
            MaxFileSizeBytes = 10_485_760,
            AllowedExtensions = new List<string> { ".jpg", ".jpeg", ".png", ".pdf", ".docx", ".xlsx", ".webp" },
            AllowedMimeTypes = new Dictionary<string, string>
            {
                { ".jpg",  "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".png",  "image/png"  },
                { ".pdf",  "application/pdf" }
            }
        });
        _sut = new FileValidationService(settings, NullLogger<FileValidationService>.Instance);
    }

    // =========================================================
    // Valid files
    // =========================================================

    [Fact]
    public async Task ValidateAsync_ValidJpeg_ReturnsValid()
    {
        var file = MakeJpegFile("photo.jpg");
        var result = await _sut.ValidateAsync(file);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidPdf_ReturnsValid()
    {
        var file = MakePdfFile("document.pdf");
        var result = await _sut.ValidateAsync(file);
        result.IsValid.Should().BeTrue();
    }

    // =========================================================
    // Extension validation
    // =========================================================

    [Fact]
    public async Task ValidateAsync_BlockedExtension_ReturnsInvalid()
    {
        var file = MakeFile("malware.exe", new byte[] { 0x4D, 0x5A }, "application/octet-stream");
        var result = await _sut.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAsync_DoubleExtension_ReturnsInvalid()
    {
        var file = MakeJpegFile("photo.jpg.exe");
        var result = await _sut.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NoExtension_ReturnsInvalid()
    {
        var file = MakeFile("noextension", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg");
        var result = await _sut.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
    }

    // =========================================================
    // Size validation
    // =========================================================

    [Fact]
    public async Task ValidateAsync_EmptyFile_ReturnsInvalid()
    {
        var file = MakeFile("empty.jpg", Array.Empty<byte>(), "image/jpeg");
        var result = await _sut.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_FileTooLarge_ReturnsInvalid()
    {
        var huge = new byte[11_000_000]; // > 10 MB
        var file = MakeFile("huge.jpg", huge, "image/jpeg");
        var result = await _sut.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("size");
    }

    // =========================================================
    // ValidationDetails populated
    // =========================================================

    [Fact]
    public async Task ValidateAsync_ReturnsClaimedExtensionInDetails()
    {
        var file = MakeJpegFile("photo.jpg");
        var result = await _sut.ValidateAsync(file);
        result.Details.ClaimedExtension.Should().Be(".jpg");
    }

    [Fact]
    public async Task ValidateAsync_ReturnsFileSizeInDetails()
    {
        var file = MakeJpegFile("photo.jpg");
        var result = await _sut.ValidateAsync(file);
        result.Details.FileSizeBytes.Should().BeGreaterThan(0);
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static IFormFile MakeJpegFile(string name)
    {
        var bytes = new byte[600];
        bytes[0] = 0xFF; bytes[1] = 0xD8;
        bytes[2] = 0xFF; bytes[3] = 0xE0;
        return MakeFile(name, bytes, "image/jpeg");
    }

    private static IFormFile MakePdfFile(string name)
    {
        var bytes = new byte[600];
        bytes[0] = 0x25; bytes[1] = 0x50; // %P
        bytes[2] = 0x44; bytes[3] = 0x46; // DF
        return MakeFile(name, bytes, "application/pdf");
    }

    private static IFormFile MakeFile(string name, byte[] content, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}