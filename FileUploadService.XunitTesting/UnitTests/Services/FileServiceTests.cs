using Dapper;
using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

/// <summary>
/// Unit tests for FileService.
/// Uses SQLite in-memory for database operations, mocks for ClamAV and validation.
/// </summary>
public class FileServiceTests : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly FileService _sut;
    private readonly Mock<IClamClientFactory> _clamFactory = new();
    private readonly Mock<IVirusScanClient> _clamClient = new();

    public FileServiceTests()
    {
        // ── in-memory SQLite ──────────────────────────────────────
        _db = new SqliteConnection("Data Source=:memory:");
        _db.Open();
        _db.Execute(@"
            CREATE TABLE files (
                id                INTEGER PRIMARY KEY AUTOINCREMENT,
                reference_id      TEXT    NOT NULL UNIQUE,
                original_filename TEXT    NOT NULL,
                storage_path      TEXT    NOT NULL,
                content_type      TEXT,
                file_size         INTEGER NOT NULL,
                uploaded_by       INTEGER,
                created_at        TEXT    NOT NULL,
                iv                TEXT    NOT NULL,
                is_deleted        INTEGER NOT NULL DEFAULT 0
            )");

        // ── ClamAV mock — always clean ────────────────────────────
        _clamClient.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clamClient.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                   .ReturnsAsync(VirusScanResult.Clean());
        _clamFactory.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<int>()))
                    .Returns(_clamClient.Object);

        _sut = BuildService();
    }

    // =========================================================
    // UploadFileAsync — metadata saved
    // =========================================================

    [Fact]
    public async Task UploadFileAsync_ValidFile_SavesMetadataToDatabase()
    {
        var file = MakeJpegFile("photo.jpg", 600);
        var response = await _sut.UploadFileAsync(file, null);

        var row = _db.QuerySingleOrDefault<dynamic>(
            "SELECT * FROM files WHERE reference_id = @ref",
            new { @ref = response.ReferenceId });

        ((object)row).Should().NotBeNull();
    }

    [Fact]
    public async Task UploadFileAsync_ValidFile_ReturnsReferenceId()
    {
        var file = MakeJpegFile("photo.jpg", 600);
        var response = await _sut.UploadFileAsync(file, null);
        response.ReferenceId.Should().StartWith("FILE-");
    }

    [Fact]
    public async Task UploadFileAsync_ReferenceId_HasCorrectFormat()
    {
        var file = MakeJpegFile("photo.jpg", 600);
        var response = await _sut.UploadFileAsync(file, null);
        response.ReferenceId.Should().MatchRegex(@"^FILE-\d{8}-[A-F0-9]{6}$");
    }

    [Fact]
    public async Task UploadFileAsync_TwoUploads_ProduceDifferentReferenceIds()
    {
        var r1 = await _sut.UploadFileAsync(MakeJpegFile("a.jpg", 600), null);
        var r2 = await _sut.UploadFileAsync(MakeJpegFile("b.jpg", 600), null);
        r1.ReferenceId.Should().NotBe(r2.ReferenceId);
    }

    [Fact]
    public async Task UploadFileAsync_StoresIvInDatabase()
    {
        var file = MakeJpegFile("photo.jpg", 600);
        var response = await _sut.UploadFileAsync(file, null);

        var iv = _db.QuerySingle<string>(
            "SELECT iv FROM files WHERE reference_id = @ref",
            new { @ref = response.ReferenceId });

        iv.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UploadFileAsync_ValidUploadedBy_SavesItAsLong()
    {
        var file = MakeJpegFile("photo.jpg", 600);
        var response = await _sut.UploadFileAsync(file, "42");

        var uploadedBy = _db.QuerySingle<long?>(
            "SELECT uploaded_by FROM files WHERE reference_id = @ref",
            new { @ref = response.ReferenceId });

        uploadedBy.Should().Be(42L);
    }

    [Fact]
    public async Task UploadFileAsync_NullUploadedBy_SavesNull()
    {
        var file = MakeJpegFile("photo.jpg", 600);
        var response = await _sut.UploadFileAsync(file, null);

        var uploadedBy = _db.QuerySingle<long?>(
            "SELECT uploaded_by FROM files WHERE reference_id = @ref",
            new { @ref = response.ReferenceId });

        uploadedBy.Should().BeNull();
    }

    [Fact]
    public async Task UploadFileAsync_IsDeletedDefaultsFalse()
    {
        var file = MakeJpegFile("photo.jpg", 600);
        var response = await _sut.UploadFileAsync(file, null);

        var isDeleted = _db.QuerySingle<int>(
            "SELECT is_deleted FROM files WHERE reference_id = @ref",
            new { @ref = response.ReferenceId });

        isDeleted.Should().Be(0);
    }

    [Fact]
    public async Task UploadFileAsync_EmptyFile_ThrowsFileValidationException()
    {
        var file = MakeFormFile("empty.jpg", Array.Empty<byte>(), "image/jpeg");
        var act = async () => await _sut.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<FileValidationException>();
    }

    [Fact]
    public async Task UploadFileAsync_BlockedExtension_ThrowsFileValidationException()
    {
        var file = MakeFormFile("malware.exe", new byte[] { 0x4D, 0x5A }, "application/octet-stream");
        var act = async () => await _sut.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<FileValidationException>();
    }

    [Fact]
    public async Task UploadFileAsync_ScannerUnavailable_ThrowsVirusScanException()
    {
        _clamClient.Setup(c => c.PingAsync()).ReturnsAsync(false);
        var file = MakeJpegFile("photo.jpg", 600);
        var act = async () => await _sut.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<VirusScanException>();
    }

    [Fact]
    public async Task UploadFileAsync_VirusDetected_ThrowsVirusDetectedException()
    {
        _clamClient.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                   .ReturnsAsync(VirusScanResult.Infected("Win.Test.EICAR_HDB-1"));

        var file = MakeJpegFile("photo.jpg", 600);
        var act = async () => await _sut.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<VirusDetectedException>();
    }

    // =========================================================
    // DeleteFileAsync
    // =========================================================

    [Fact]
    public async Task DeleteFileAsync_ExistingFile_ReturnsTrue()
    {
        InsertFile("FILE-DEL-001");
        var result = await _sut.DeleteFileAsync("FILE-DEL-001");
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteFileAsync_ExistingFile_SetsIsDeletedTrue()
    {
        InsertFile("FILE-DEL-002");
        await _sut.DeleteFileAsync("FILE-DEL-002");

        var isDeleted = _db.QuerySingle<int>(
            "SELECT is_deleted FROM files WHERE reference_id = 'FILE-DEL-002'");
        isDeleted.Should().Be(1);
    }

    [Fact]
    public async Task DeleteFileAsync_NonExistentFile_ReturnsFalse()
    {
        var result = await _sut.DeleteFileAsync("FILE-NONEXISTENT-999");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteFileAsync_AlreadyDeletedFile_ReturnsFalse()
    {
        InsertFile("FILE-DEL-003", isDeleted: 1);
        var result = await _sut.DeleteFileAsync("FILE-DEL-003");
        result.Should().BeFalse();
    }

    // =========================================================
    // GetFileMetadataAsync
    // =========================================================

    [Fact]
    public async Task GetFileMetadataAsync_ExistingFile_ReturnsMetadata()
    {
        InsertFile("FILE-META-001");
        var metadata = await _sut.GetFileMetadataAsync("FILE-META-001");
        metadata.Should().NotBeNull();
        metadata!.ReferenceId.Should().Be("FILE-META-001");
    }

    [Fact]
    public async Task GetFileMetadataAsync_NonExistentFile_ReturnsNull()
    {
        var metadata = await _sut.GetFileMetadataAsync("FILE-NOTFOUND-999");
        metadata.Should().BeNull();
    }

    [Fact]
    public async Task GetFileMetadataAsync_SoftDeletedFile_ReturnsNull()
    {
        InsertFile("FILE-SDEL-001", isDeleted: 1);
        var metadata = await _sut.GetFileMetadataAsync("FILE-SDEL-001");
        metadata.Should().BeNull();
    }

    // =========================================================
    // Helpers
    // =========================================================

    private FileService BuildService()
    {
        var storageSettings = Options.Create(new FileStorageSettings
        {
            BasePath = Path.Combine(Path.GetTempPath(), "fus-tests"),
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

        var encSettings = Options.Create(new EncryptionSettings
        {
            AesKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "ConnectionStrings:DefaultConnection", "Data Source=:memory:" }
            })
            .Build();

        var validator = new FileValidationService(
            storageSettings,
            NullLogger<FileValidationService>.Instance);

        var virusScanner = new VirusScanService(
            Options.Create(new ClamAvSettings()),
            NullLogger<VirusScanService>.Instance,
            _clamFactory.Object);

        var encryptionService = new EncryptionService(
            encSettings,
            NullLogger<EncryptionService>.Instance);

        return new FileService(
            storageSettings,
            config,
            NullLogger<FileService>.Instance,
            validator,
            virusScanner,
            encryptionService,
            _db);  // inject SQLite test connection
    }

    /// <summary>Build a JPEG IFormFile with valid magic bytes.</summary>
    private static IFormFile MakeJpegFile(string name, int size)
    {
        var bytes = new byte[size];
        bytes[0] = 0xFF; bytes[1] = 0xD8;
        bytes[2] = 0xFF; bytes[3] = 0xE0;
        return MakeFormFile(name, bytes, "image/jpeg");
    }

    private static IFormFile MakeFormFile(string name, byte[] content, string contentType)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    private void InsertFile(string referenceId, int isDeleted = 0)
    {
        _db.Execute(@"
            INSERT INTO files
                (reference_id, original_filename, storage_path, content_type,
                 file_size, uploaded_by, created_at, iv, is_deleted)
            VALUES
                (@ref, 'test.pdf', 'uploads/test.enc', 'application/pdf',
                 1024, NULL, @now, @iv, @del)",
            new
            {
                @ref = referenceId,
                @now = DateTime.UtcNow.ToString("o"),
                @iv = Convert.ToBase64String(new byte[16]),
                @del = isDeleted
            });
    }

    public void Dispose()
    {
        _db.Close();
        _db.Dispose();
    }
}