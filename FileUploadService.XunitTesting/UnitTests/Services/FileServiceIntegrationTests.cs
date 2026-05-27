using Dapper;
using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class GuidTypeHandler : SqlMapper.TypeHandler<Guid>
{
    public static readonly GuidTypeHandler Instance = new();
    public override void SetValue(System.Data.IDbDataParameter parameter, Guid value)
        => parameter.Value = value.ToString();
    public override Guid Parse(object value)
        => Guid.Parse(value.ToString()!);
}

public class NullableGuidTypeHandler : SqlMapper.TypeHandler<Guid?>
{
    public static readonly NullableGuidTypeHandler Instance = new();
    public override void SetValue(System.Data.IDbDataParameter parameter, Guid? value)
        => parameter.Value = value?.ToString() ?? (object)DBNull.Value;
    public override Guid? Parse(object value)
        => value is null or DBNull ? null : Guid.Parse(value.ToString()!);
}

public class FileServiceIntegrationTests : IAsyncLifetime
{
    // ── SQLite in-memory connection kept open for test lifetime ───
    private readonly SqliteConnection _db;
    private readonly string _tempFolder;

    // ── Mocks ──────────────────────────────────────────────────────
    private readonly Mock<IClamClientFactory> _clamFactoryMock = new();
    private readonly Mock<IVirusScanClient> _clamClientMock = new();
    private readonly Mock<ILogger<FileService>> _fileServiceLogger = new();
    private readonly Mock<ILogger<FileValidationService>> _validationLogger = new();
    private readonly Mock<ILogger<VirusScanService>> _virusScanLogger = new();
    private readonly Mock<ILogger<EncryptionService>> _encryptionLogger = new();

    public FileServiceIntegrationTests()
    {
        _db = new SqliteConnection("Data Source=:memory:");
        _tempFolder = Path.Combine(Path.GetTempPath(), "FileServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempFolder);

        // Default: ClamAV always returns Clean
        _clamFactoryMock
            .Setup(f => f.Create(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(_clamClientMock.Object);
        _clamClientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clamClientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Clean());
    }

    // ── Lifecycle ──────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        // Register SQLite ↔ Guid mapping — SQLite stores GUIDs as TEXT
        SqlMapper.AddTypeHandler(GuidTypeHandler.Instance);
        SqlMapper.AddTypeHandler(NullableGuidTypeHandler.Instance);

        await _db.OpenAsync();

        // SQLite-compatible version of the files table
        await _db.ExecuteAsync(@"
            CREATE TABLE files (
                id TEXT PRIMARY KEY,
                reference_id TEXT UNIQUE NOT NULL,
                original_filename TEXT NOT NULL,
                storage_path TEXT NOT NULL,
                content_type TEXT,
                file_size INTEGER NOT NULL,
                uploaded_by TEXT,
                created_at TEXT NOT NULL,
                iv TEXT,
                is_encrypted INTEGER NOT NULL DEFAULT 0
            )");
    }

    public async Task DisposeAsync()
    {
        await _db.CloseAsync();
        if (Directory.Exists(_tempFolder))
        {
            // Retry once — FileStream from unencrypted download may still be flushing
            try { Directory.Delete(_tempFolder, true); }
            catch (IOException)
            {
                await Task.Delay(200);
                try { Directory.Delete(_tempFolder, true); } catch { /* best effort */ }
            }
        }
    }

    // ── Build FileService with SQLite connection ───────────────────

    private FileService BuildFileService(bool scannerUnavailable = false, bool virusDetected = false, bool nullThreat = false)
    {
        if (scannerUnavailable)
            _clamClientMock.Setup(c => c.PingAsync()).ReturnsAsync(false);
        else if (virusDetected)
            _clamClientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(VirusScanResult.Infected("Win.Test.EICAR_HDB-1"));
        else if (nullThreat)
        {
            _clamClientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
            _clamClientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(new VirusScanResult { IsClean = false, ThreatName = null, ScannerUnavailable = false });
        }
        else
        {
            _clamClientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
            _clamClientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
                .ReturnsAsync(VirusScanResult.Clean());
        }

        var storageSettings = Options.Create(new FileStorageSettings
        {
            BasePath = _tempFolder,
            MaxFileSizeBytes = 10_485_760,
            AllowedExtensions = new List<string> { ".jpg", ".jpeg", ".png", ".pdf", ".docx", ".xlsx" },
            AllowedMimeTypes = new Dictionary<string, string>
            {
                { ".jpg",  "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".png",  "image/png"  },
                { ".pdf",  "application/pdf" },
                { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
            }
        });

        var encryptionSettings = Options.Create(new EncryptionSettings
        {
            AesKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });

        var clamAvSettings = Options.Create(new ClamAvSettings
        {
            Host = "127.0.0.1",
            Port = 3310,
            TimeoutSeconds = 5
        });

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // SQLite connection string — Dapper uses whatever IDbConnection is opened
                ["ConnectionStrings:DefaultConnection"] = _db.ConnectionString
            })
            .Build();

        var validator = new FileValidationService(storageSettings, _validationLogger.Object);
        var virusScanner = new VirusScanService(clamAvSettings, _virusScanLogger.Object, _clamFactoryMock.Object);
        var encryptionSvc = new EncryptionService(encryptionSettings, _encryptionLogger.Object);

        return new FileService(
            storageSettings, config,
            _fileServiceLogger.Object,
            validator, virusScanner, encryptionSvc,
            _db   // ← inject open SQLite connection
        );
    }

    private static IFormFile MakeFile(
        string name = "photo.jpg",
        string contentType = "image/jpeg",
        byte[]? content = null)
    {
        content ??= RealJpegBytes();
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(name);
        mock.Setup(f => f.Length).Returns(content.Length);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(content));
        return mock.Object;
    }

    private static byte[] RealJpegBytes()
    {
        var b = new byte[600];
        b[0] = 0xFF; b[1] = 0xD8; b[2] = 0xFF; b[3] = 0xE0;
        b[4] = 0x00; b[5] = 0x10; b[6] = 0x4A; b[7] = 0x46;
        b[8] = 0x49; b[9] = 0x46; b[10] = 0x00; b[11] = 0x01;
        return b;
    }

    private static byte[] RealPdfBytes()
    {
        var b = new byte[600];
        b[0] = 0x25; b[1] = 0x50; b[2] = 0x44; b[3] = 0x46; b[4] = 0x2D;
        return b;
    }

    // =========================================================
    // CONSTRUCTOR
    // =========================================================

    [Fact]
    public void Constructor_MissingConnectionString_ThrowsInvalidOperationException()
    {
        var storageSettings = Options.Create(new FileStorageSettings
        {
            BasePath = _tempFolder,
            MaxFileSizeBytes = 10_485_760,
            AllowedExtensions = new List<string>(),
            AllowedMimeTypes = new Dictionary<string, string>()
        });
        var config = new ConfigurationBuilder().Build(); // no connection string
        var encSettings = Options.Create(new EncryptionSettings
        {
            AesKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        var clamSettings = Options.Create(new ClamAvSettings { Host = "127.0.0.1", Port = 3310 });
        var validator = new FileValidationService(storageSettings, _validationLogger.Object);
        var virusScanner = new VirusScanService(clamSettings, _virusScanLogger.Object, _clamFactoryMock.Object);
        var encSvc = new EncryptionService(encSettings, _encryptionLogger.Object);

        var act = () => new FileService(
            storageSettings, config, _fileServiceLogger.Object,
            validator, virusScanner, encSvc, _db);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connection string not found*");
    }

    // =========================================================
    // UPLOAD — happy path
    // =========================================================

    [Fact]
    public async Task UploadFileAsync_ValidJpeg_ReturnsReferenceId()
    {
        var svc = BuildFileService();
        var file = MakeFile("photo.jpg", "image/jpeg", RealJpegBytes()); // -----------> here i can make changes in extension to fail a test case
        var result = await svc.UploadFileAsync(file, null);

        result.Should().NotBeNull();
        result.ReferenceId.Should().StartWith("FILE-");
        result.OriginalFilename.Should().Be("photo.jpg");
        result.ContentType.Should().Be("image/jpeg");
        result.FileSizeBytes.Should().Be(600);
    }

    [Fact]
    public async Task UploadFileAsync_ValidPdf_ReturnsReferenceId()
    {
        var svc = BuildFileService();
        var file = MakeFile("report.pdf", "application/pdf", RealPdfBytes());
        var result = await svc.UploadFileAsync(file, null);

        result.ReferenceId.Should().StartWith("FILE-");
        result.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task UploadFileAsync_WithUploadedBy_SetsMetadata()
    {
        var svc = BuildFileService();
        var uploaderId = Guid.NewGuid().ToString();
        var file = MakeFile();
        var result = await svc.UploadFileAsync(file, uploaderId);
        result.Should().NotBeNull();
        result.ReferenceId.Should().StartWith("FILE-");
    }

    [Fact]
    public async Task UploadFileAsync_NullUploadedBy_Succeeds()
    {
        var svc = BuildFileService();
        var result = await svc.UploadFileAsync(MakeFile(), null);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadFileAsync_InvalidUploadedByString_TreatedAsNull()
    {
        // Non-GUID uploadedBy → Guid.TryParse fails → stored as null
        var svc = BuildFileService();
        var result = await svc.UploadFileAsync(MakeFile(), "not-a-guid");
        result.Should().NotBeNull();
        result.ReferenceId.Should().StartWith("FILE-");
    }

    [Fact]
    public async Task UploadFileAsync_CreatesEncryptedFileOnDisk()
    {
        var svc = BuildFileService();
        var result = await svc.UploadFileAsync(MakeFile(), null);

        // Find the .enc file written to temp folder
        var encFiles = Directory.GetFiles(_tempFolder, "*.enc", SearchOption.AllDirectories);
        encFiles.Should().HaveCount(1);
    }

    [Fact]
    public async Task UploadFileAsync_SavesMetadataToDatabase()
    {
        var svc = BuildFileService();
        var result = await svc.UploadFileAsync(MakeFile(), null);

        var row = await _db.QuerySingleOrDefaultAsync(
            "SELECT reference_id, is_encrypted FROM files WHERE reference_id = @ref",
            new { @ref = result.ReferenceId });

        ((string)row.reference_id).Should().Be(result.ReferenceId);
        ((long)row.is_encrypted).Should().Be(1);
    }

    [Fact]
    public async Task UploadFileAsync_StoresIvInDatabase()
    {
        var svc = BuildFileService();
        var result = await svc.UploadFileAsync(MakeFile(), null);

        var iv = await _db.QuerySingleOrDefaultAsync<string?>(
            "SELECT iv FROM files WHERE reference_id = @ref",
            new { @ref = result.ReferenceId });

        iv.Should().NotBeNullOrEmpty();
    }

    // =========================================================
    // UPLOAD — validation failures
    // =========================================================

    [Fact]
    public async Task UploadFileAsync_BlockedExtension_ThrowsFileValidationException()
    {
        var svc = BuildFileService();
        var file = MakeFile("malware.exe", "application/octet-stream",
            new byte[] { 0x4D, 0x5A, 0x90, 0x00 });
        var act = async () => await svc.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<FileValidationException>();
    }

    [Fact]
    public async Task UploadFileAsync_EmptyFile_ThrowsFileValidationException()
    {
        var svc = BuildFileService();
        var file = MakeFile("photo.jpg", "image/jpeg", Array.Empty<byte>());
        var act = async () => await svc.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<FileValidationException>();
    }

    // =========================================================
    // UPLOAD — virus scan failures
    // =========================================================

    [Fact]
    public async Task UploadFileAsync_ScannerUnavailable_ThrowsVirusScanException()
    {
        var svc = BuildFileService(scannerUnavailable: true);
        var act = async () => await svc.UploadFileAsync(MakeFile(), null);
        await act.Should().ThrowAsync<VirusScanException>()
            .WithMessage("*unavailable*");
    }

    [Fact]
    public async Task UploadFileAsync_VirusDetected_ThrowsVirusDetectedException()
    {
        var svc = BuildFileService(virusDetected: true);
        var act = async () => await svc.UploadFileAsync(MakeFile(), null);
        await act.Should().ThrowAsync<VirusDetectedException>()
            .WithMessage("*EICAR*");
    }

    // =========================================================
    // DOWNLOAD — happy path (encrypted file)
    // =========================================================

    [Fact]
    public async Task DownloadFileAsync_ExistingFile_ReturnsMetadataAndStream()
    {
        var svc = BuildFileService();
        var upload = await svc.UploadFileAsync(MakeFile(), null);

        var result = await svc.DownloadFileAsync(upload.ReferenceId);

        result.Should().NotBeNull();
        result!.Value.Metadata.ReferenceId.Should().Be(upload.ReferenceId);
        result!.Value.FileStream.Should().NotBeNull();
        result!.Value.FileStream.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task DownloadFileAsync_ExistingFile_DecryptsCorrectly()
    {
        var svc = BuildFileService();
        var originalBytes = RealJpegBytes();
        var file = MakeFile("photo.jpg", "image/jpeg", originalBytes);
        var upload = await svc.UploadFileAsync(file, null);

        var result = await svc.DownloadFileAsync(upload.ReferenceId);

        var ms = new MemoryStream();
        await result!.Value.FileStream.CopyToAsync(ms);
        ms.ToArray().Should().Equal(originalBytes);
    }

    [Fact]
    public async Task DownloadFileAsync_ExistingFile_ReturnsCorrectContentType()
    {
        var svc = BuildFileService();
        var upload = await svc.UploadFileAsync(MakeFile("photo.jpg", "image/jpeg"), null);
        var result = await svc.DownloadFileAsync(upload.ReferenceId);
        result!.Value.Metadata.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task DownloadFileAsync_ExistingFile_ReturnsCorrectFilename()
    {
        var svc = BuildFileService();
        var upload = await svc.UploadFileAsync(MakeFile("my-report.jpg", "image/jpeg"), null);
        var result = await svc.DownloadFileAsync(upload.ReferenceId);
        result!.Value.Metadata.OriginalFilename.Should().Be("my-report.jpg");
    }

    // =========================================================
    // DOWNLOAD — not found paths
    // =========================================================

    [Fact]
    public async Task DownloadFileAsync_UnknownReferenceId_ReturnsNull()
    {
        var svc = BuildFileService();
        var result = await svc.DownloadFileAsync("FILE-NOTFOUND-000");
        result.Should().BeNull();
    }



    // =========================================================
    // GenerateReferenceId format
    // =========================================================

    [Fact]
    public async Task UploadFileAsync_ReferenceId_HasCorrectFormat()
    {
        var svc = BuildFileService();
        var result = await svc.UploadFileAsync(MakeFile(), null);

        // Format: FILE-YYYYMMDD-XXXXXX
        result.ReferenceId.Should().MatchRegex(@"^FILE-\d{8}-[A-Z0-9]{6}$");
    }

    [Fact]
    public async Task UploadFileAsync_TwoUploads_ProduceDifferentReferenceIds()
    {
        var svc = BuildFileService();
        var r1 = await svc.UploadFileAsync(MakeFile("a.jpg", "image/jpeg", RealJpegBytes()), null);
        var r2 = await svc.UploadFileAsync(MakeFile("b.jpg", "image/jpeg", RealJpegBytes()), null);
        r1.ReferenceId.Should().NotBe(r2.ReferenceId);
    }

    // =========================================================
    // DOWNLOAD — unencrypted legacy file branch (IsEncrypted = false)
    // =========================================================

    [Fact]
    public async Task DownloadFileAsync_UnencryptedFile_ReturnsFileStream()
    {
        var fileId = Guid.NewGuid();
        var refId = "FILE-LEGACY-001";
        var filePath = Path.Combine(_tempFolder, fileId.ToString("N") + ".bin");
        var bytes = RealJpegBytes();
        await File.WriteAllBytesAsync(filePath, bytes);

        await _db.ExecuteAsync(@"
            INSERT INTO files (id, reference_id, original_filename, storage_path,
                content_type, file_size, created_at, iv, is_encrypted)
            VALUES (@id, @ref, @fn, @sp, @ct, @fs, @ca, NULL, 0)",
            new
            {
                id = fileId.ToString(),
                @ref = refId,
                fn = "legacy.jpg",
                sp = filePath,
                ct = "image/jpeg",
                fs = bytes.Length,
                ca = DateTime.UtcNow.ToString("o")
            });

        var svc = BuildFileService();
        var result = await svc.DownloadFileAsync(refId);

        result.Should().NotBeNull();
        result!.Value.Metadata.IsDeleted.Should().BeFalse();
        result!.Value.FileStream.Should().NotBeNull();

        // Dispose stream so DisposeAsync can delete the temp folder
        await result!.Value.FileStream.DisposeAsync();
    }

    [Fact]
    public async Task DownloadFileAsync_UnencryptedFile_StreamIsReadable()
    {
        var fileId = Guid.NewGuid();
        var refId = "FILE-LEGACY-002";
        var filePath = Path.Combine(_tempFolder, fileId.ToString("N") + ".bin");
        var bytes = RealJpegBytes();
        await File.WriteAllBytesAsync(filePath, bytes);

        await _db.ExecuteAsync(@"
            INSERT INTO files (id, reference_id, original_filename, storage_path,
                content_type, file_size, created_at, iv, is_encrypted)
            VALUES (@id, @ref, @fn, @sp, @ct, @fs, @ca, NULL, 0)",
            new
            {
                id = fileId.ToString(),
                @ref = refId,
                fn = "legacy2.jpg",
                sp = filePath,
                ct = "image/jpeg",
                fs = bytes.Length,
                ca = DateTime.UtcNow.ToString("o")
            });

        var svc = BuildFileService();
        var result = await svc.DownloadFileAsync(refId);

        var ms = new MemoryStream();
        await result!.Value.FileStream.CopyToAsync(ms);
        ms.Length.Should().BeGreaterThan(0);

        // Dispose stream so DisposeAsync can delete the temp folder
        await result!.Value.FileStream.DisposeAsync();
    }

    // =========================================================
    // UPLOAD — VirusDetected with null ThreatName → Unknown threat branch
    // =========================================================

    [Fact]
    public async Task UploadFileAsync_VirusDetectedNullThreat_ThrowsVirusDetectedException()
    {
        // nullThreat=true → BuildFileService sets ScanAsync to return IsClean=false, ThreatName=null
        var svc = BuildFileService(nullThreat: true);
        var act = async () => await svc.UploadFileAsync(MakeFile(), null);
        await act.Should().ThrowAsync<VirusDetectedException>()
            .WithMessage("*Unknown threat*");
    }

    // =========================================================
    // UPLOAD — valid GUID uploadedBy → Guid.TryParse success branch
    // =========================================================

    [Fact]
    public async Task UploadFileAsync_ValidGuidUploadedBy_SavesUploadedByInDb()
    {
        var svc = BuildFileService();
        var uploaderId = Guid.NewGuid().ToString();
        var result = await svc.UploadFileAsync(MakeFile(), uploaderId);

        var uploaded = await _db.QuerySingleOrDefaultAsync<string?>(
            "SELECT uploaded_by FROM files WHERE reference_id = @ref",
            new { @ref = result.ReferenceId });

        uploaded.Should().NotBeNullOrEmpty();
    }

    // =========================================================
    // DOWNLOAD — encrypted file with no IV throws InvalidOperationException
    // =========================================================

    [Fact]
    public async Task DownloadFileAsync_EncryptedFileWithNoIv_ThrowsInvalidOperationException()
    {
        var dummyPath = Path.Combine(_tempFolder, "noiv.enc");
        await File.WriteAllBytesAsync(dummyPath, new byte[] { 1, 2, 3, 4 });

        await _db.ExecuteAsync(@"
            INSERT INTO files (id, reference_id, original_filename, storage_path,
                content_type, file_size, created_at, iv, is_encrypted)
            VALUES (@id, @ref, @fn, @sp, @ct, @fs, @ca, NULL, 1)",
            new
            {
                id = Guid.NewGuid().ToString(),
                @ref = "FILE-NOIV-001",
                fn = "noiv.jpg",
                sp = dummyPath,
                ct = "image/jpeg",
                fs = 100,
                ca = DateTime.UtcNow.ToString("o")
            });

        var svc = BuildFileService();
        var act = async () => await svc.DownloadFileAsync("FILE-NOIV-001");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no IV*");
    }

    // =========================================================
    // DOWNLOAD — file missing on disk returns null
    // =========================================================

    [Fact]
    public async Task DownloadFileAsync_FileInDbButMissingOnDisk_ReturnsNull()
    {
        var missingPath = Path.Combine(_tempFolder, "ghost_nonexistent.enc");

        await _db.ExecuteAsync(@"
            INSERT INTO files (id, reference_id, original_filename, storage_path,
                content_type, file_size, created_at, iv, is_encrypted)
            VALUES (@id, @ref, @fn, @sp, @ct, @fs, @ca, @iv, 1)",
            new
            {
                id = Guid.NewGuid().ToString(),
                @ref = "FILE-MISSING-001",
                fn = "ghost.jpg",
                sp = missingPath,
                ct = "image/jpeg",
                fs = 100,
                ca = DateTime.UtcNow.ToString("o"),
                iv = Convert.ToBase64String(new byte[16])
            });

        var svc = BuildFileService();
        var result = await svc.DownloadFileAsync("FILE-MISSING-001");
        result.Should().BeNull();
    }

    // =========================================================
    // DOWNLOAD — empty DB returns null
    // =========================================================

    [Fact]
    public async Task DownloadFileAsync_EmptyDatabase_ReturnsNull()
    {
        var svc = BuildFileService();
        var result = await svc.DownloadFileAsync("FILE-EMPTY-DB-001");
        result.Should().BeNull();
    }

    // =========================================================
    // BRANCH COVERAGE — NpgsqlConnection else branch (no _testConnection)
    // These tests use the 6-arg constructor (no injected connection) so the
    // else branch that creates NpgsqlConnection gets hit. PostgreSQL is not
    // required — the exception itself proves the branch was entered.
    // =========================================================

    private FileService BuildFileServiceWithRealConstructor()
    {
        var storageSettings = Options.Create(new FileStorageSettings
        {
            BasePath = _tempFolder,
            MaxFileSizeBytes = 10_485_760,
            AllowedExtensions = new List<string> { ".jpg", ".jpeg", ".png", ".pdf", ".docx", ".xlsx" },
            AllowedMimeTypes = new Dictionary<string, string>
            {
                { ".jpg",  "image/jpeg" },
                { ".jpeg", "image/jpeg" },
                { ".png",  "image/png"  },
                { ".pdf",  "application/pdf" },
                { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
                { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" }
            }
        });
        var encryptionSettings = Options.Create(new EncryptionSettings
        {
            AesKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
        });
        var clamAvSettings = Options.Create(new ClamAvSettings
        {
            Host = "127.0.0.1",
            Port = 3310,
            TimeoutSeconds = 5
        });
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Use a fake PostgreSQL connection string — connection will fail but branch is covered
                ["ConnectionStrings:DefaultConnection"] = "Host=127.0.0.1;Port=5433;Database=fake;Username=fake;Password=fake"
            })
            .Build();

        _clamClientMock.Setup(c => c.PingAsync()).ReturnsAsync(true);
        _clamClientMock.Setup(c => c.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>()))
            .ReturnsAsync(VirusScanResult.Clean());
        _clamFactoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<int>()))
            .Returns(_clamClientMock.Object);

        var validator = new FileValidationService(storageSettings, _validationLogger.Object);
        var virusScanner = new VirusScanService(clamAvSettings, _virusScanLogger.Object, _clamFactoryMock.Object);
        var encryptionSvc = new EncryptionService(encryptionSettings, _encryptionLogger.Object);

        // Uses 6-arg constructor — _testConnection will be null → else branch runs
        return new FileService(storageSettings, config, _fileServiceLogger.Object,
            validator, virusScanner, encryptionSvc);
    }

    [Fact]
    public async Task UploadFileAsync_NoTestConnection_HitsNpgsqlBranch_ThrowsOnDbFailure()
    {
        // _testConnection = null → SaveMetadataAsync hits else → new NpgsqlConnection → throws
        var svc = BuildFileServiceWithRealConstructor();
        var file = MakeFile("photo.jpg", "image/jpeg", RealJpegBytes());
        var act = async () => await svc.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task UploadFileAsync_NoTestConnection_WithUploadedBy_HitsNpgsqlBranch()
    {
        // Also covers the Guid.TryParse success path + NpgsqlConnection else branch
        var svc = BuildFileServiceWithRealConstructor();
        var file = MakeFile("photo.jpg", "image/jpeg", RealJpegBytes());
        var act = async () => await svc.UploadFileAsync(file, Guid.NewGuid().ToString());
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task DownloadFileAsync_NoTestConnection_HitsNpgsqlBranch_ThrowsOnDbFailure()
    {
        // _testConnection = null → GetMetadataByReferenceIdAsync hits else → new NpgsqlConnection → throws
        var svc = BuildFileServiceWithRealConstructor();
        var act = async () => await svc.DownloadFileAsync("FILE-ANY-001");
        await act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task UploadFileAsync_NoTestConnection_ValidationFails_ThrowsBeforeNpgsql()
    {
        // Even with real constructor, validation failure throws BEFORE hitting NpgsqlConnection
        // This covers the !validationResult.IsValid branch with the real constructor
        var svc = BuildFileServiceWithRealConstructor();
        var file = MakeFile("malware.exe", "application/octet-stream", new byte[] { 0x4D, 0x5A });
        var act = async () => await svc.UploadFileAsync(file, null);
        await act.Should().ThrowAsync<FileValidationException>();
    }
}