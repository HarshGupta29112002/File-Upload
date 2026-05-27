using FileUploadService.Application.Configurations;
using FileUploadService.Application.Implementation;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class FileValidationServiceTests
{
    private readonly Mock<ILogger<FileValidationService>> _loggerMock = new();

    private FileValidationService BuildService(
        long maxFileSize = 10_485_760,
        List<string>? allowedExtensions = null,
        Dictionary<string, string>? allowedMimeTypes = null)
    {
        var settings = Options.Create(new FileStorageSettings
        {
            BasePath = "uploads",
            MaxFileSizeBytes = maxFileSize,
            AllowedExtensions = allowedExtensions ?? new List<string>
            {
                ".jpg", ".jpeg", ".png", ".webp", ".pdf", ".docx", ".xlsx"
            },
            AllowedMimeTypes = allowedMimeTypes ?? new Dictionary<string, string>
            {
                { ".jpg",  "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".png",  "image/png"  },
                { ".webp", "image/webp" },
                { ".pdf",  "application/pdf" },
                { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
            }
        });
        return new FileValidationService(settings, _loggerMock.Object);
    }

    // ── real file header bytes used across tests ──────────────────────────────

    // JPEG: FF D8 FF E0 (JFIF) followed by enough padding
    private static byte[] RealJpegBytes()
    {
        var bytes = new byte[600];
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE0;
        bytes[4] = 0x00; bytes[5] = 0x10; bytes[6] = 0x4A; bytes[7] = 0x46;
        bytes[8] = 0x49; bytes[9] = 0x46; bytes[10] = 0x00; bytes[11] = 0x01;
        return bytes;
    }

    // PNG: 89 50 4E 47 0D 0A 1A 0A
    private static byte[] RealPngBytes()
    {
        var bytes = new byte[600];
        bytes[0] = 0x89; bytes[1] = 0x50; bytes[2] = 0x4E; bytes[3] = 0x47;
        bytes[4] = 0x0D; bytes[5] = 0x0A; bytes[6] = 0x1A; bytes[7] = 0x0A;
        return bytes;
    }

    // PDF: 25 50 44 46 2D ("%%PDF-")
    private static byte[] RealPdfBytes()
    {
        var bytes = new byte[600];
        bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;
        bytes[4] = 0x2D; bytes[5] = 0x31; bytes[6] = 0x2E; bytes[7] = 0x34;
        return bytes;
    }

    // ZIP (used by docx/xlsx): 50 4B 03 04
    private static byte[] RealZipBytes()
    {
        var bytes = new byte[600];
        bytes[0] = 0x50; bytes[1] = 0x4B; bytes[2] = 0x03; bytes[3] = 0x04;
        return bytes;
    }

    // EXE (Windows PE): 4D 5A ("MZ")
    private static byte[] ExeBytes() => new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 };

    private static IFormFile MakeFile(string filename, string contentType, byte[] content)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(filename);
        mock.Setup(f => f.Length).Returns(content.Length);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(content));
        return mock.Object;
    }


    [Fact]
    public async Task ValidateAsync_EmptyFile_ReturnsInvalid()
    {
        var svc = BuildService();
        var file = MakeFile("photo.jpg", "image/jpeg", Array.Empty<byte>());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("empty");
    }

    [Fact]
    public async Task ValidateAsync_FileTooLarge_ReturnsInvalid()
    {
        var svc = BuildService(maxFileSize: 100); // 100 bytes limit
        var file = MakeFile("photo.jpg", "image/jpeg", new byte[200]);
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("exceeds");
    }

    [Fact]
    public async Task ValidateAsync_FileSizeExactlyAtLimit_PassesSizeCheck()
    {
        // exactly at limit should pass
        var bytes = RealJpegBytes();
        var svc = BuildService(maxFileSize: bytes.Length);
        var file = MakeFile("photo.jpg", "image/jpeg", bytes);
        var result = await svc.ValidateAsync(file);
        // may fail later layers but must not fail on size
        result.FailureReason.Should().NotContain("exceeds");
    }


    [Fact]
    public async Task ValidateAsync_NullFilename_ReturnsInvalid()
    {
        var svc = BuildService();
        var file = MakeFile("", "image/jpeg", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("missing");
    }

    [Fact]
    public async Task ValidateAsync_BlockedExtension_ReturnsInvalid()
    {
        var svc = BuildService();
        var file = MakeFile("malware.exe", "application/octet-stream", ExeBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("not allowed");
    }

    [Fact]
    public async Task ValidateAsync_NoExtension_ReturnsInvalid()
    {
        var svc = BuildService();
        var file = MakeFile("filenoextension", "image/jpeg", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("no extension");
    }

    [Theory]
    [InlineData("photo.jpg.exe")]
    [InlineData("invoice.pdf.bat")]
    [InlineData("image.png.js")]
    [InlineData("doc.docx.cmd")]
    public async Task ValidateAsync_DoubleExtension_ReturnsInvalid(string filename)
    {
        var svc = BuildService();
        var file = MakeFile(filename, "image/jpeg", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("multiple extensions");
    }


    [Fact]
    public async Task ValidateAsync_GenericOctetStreamMime_SkipsMimeCheck()
    {

        var svc = BuildService();
        var file = MakeFile("photo.jpg", "application/octet-stream", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        // Should pass all layers since JPEG bytes match .jpg extension
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_MimeMismatchWithExtension_ReturnsInvalid()
    {
        var svc = BuildService();
        // extension says .jpg but MIME says application/pdf
        var file = MakeFile("photo.jpg", "application/pdf", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("MIME type mismatch");
    }

    [Fact]
    public async Task ValidateAsync_ExtensionWithNoMimeMapping_SkipsMimeCheck()
    {
        // Add .webp to allowed extensions but give it no MIME mapping
        var svc = BuildService(
            allowedExtensions: new List<string> { ".webp" },
            allowedMimeTypes: new Dictionary<string, string>() // no .webp mapping
        );
        // Can't easily fake real WEBP bytes without a file — just verify it doesn't crash
        var file = MakeFile("image.webp", "image/webp", new byte[10]);
        var result = await svc.ValidateAsync(file);
        // Will fail at magic bytes (random bytes not recognized) but not at MIME check
        result.FailureReason.Should().NotContain("MIME type mismatch");
    }

    [Fact]
    public async Task ValidateAsync_NullMimeType_SkipsMimeCheck()
    {
        var svc = BuildService();
        var file = MakeFile("photo.jpg", null!, RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        // null MIME = skip, should proceed to magic bytes
        result.FailureReason.Should().NotContain("MIME type mismatch");
    }

    // =========================================================
    // LAYER 3 — MAGIC BYTES
    // =========================================================

    [Fact]
    public async Task ValidateAsync_UnknownMagicBytes_ReturnsInvalid()
    {
        var svc = BuildService();
        // Random bytes that match no known file format
        var file = MakeFile("photo.jpg", "image/jpeg", new byte[] { 0x00, 0x01, 0x02, 0x03 });
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ExeMagicBytesWithJpgExtension_ReturnsInvalid()
    {
        var svc = BuildService();
        // MZ header (EXE) disguised as .jpg
        var file = MakeFile("photo.jpg", "image/jpeg", ExeBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_PdfMagicBytesWithJpgExtension_ReturnsInvalid()
    {
        var svc = BuildService();
        // PDF bytes but .jpg extension — should fail cross-check
        var file = MakeFile("photo.jpg", "image/jpeg", RealPdfBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeFalse();
    }


    [Fact]
    public async Task ValidateAsync_DocxWithZipMagicBytes_DoesNotFailOnMimeCheck()
    {
        var svc = BuildService();
        var file = MakeFile("report.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            RealZipBytes());
        var result = await svc.ValidateAsync(file);
        result.FailureReason.Should().NotContain("MIME type mismatch");
    }

    [Fact]
    public async Task ValidateAsync_XlsxWithZipMagicBytes_DoesNotFailOnMimeCheck()
    {
        var svc = BuildService();
        var file = MakeFile("data.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            RealZipBytes());
        var result = await svc.ValidateAsync(file);
        result.FailureReason.Should().NotContain("MIME type mismatch");
    }

    [Fact]
    public async Task ValidateAsync_ValidJpeg_ReturnsValid()
    {
        var svc = BuildService();
        var file = MakeFile("photo.jpeg", "image/jpeg", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_ValidJpegWithJpegExtension_ReturnsValid()
    {
        var svc = BuildService();
        var file = MakeFile("photo.jpeg", "image/jpeg", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidPng_ReturnsValid()
    {
        var svc = BuildService();
        var file = MakeFile("image.png", "image/png", RealPngBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidPdf_ReturnsValid()
    {
        var svc = BuildService();
        var file = MakeFile("document.pdf", "application/pdf", RealPdfBytes());
        var result = await svc.ValidateAsync(file);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidDetails_PopulatedCorrectly()
    {
        var svc = BuildService();
        var file = MakeFile("photo.jpg", "image/jpeg", RealJpegBytes());
        var result = await svc.ValidateAsync(file);
        result.Details.ClaimedExtension.Should().Be(".jpg");
        result.Details.ClaimedMimeType.Should().Be("image/jpeg");
        result.Details.FileSizeBytes.Should().BeGreaterThan(0);
    }
}