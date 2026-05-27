using Dapper;
using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;
using SystemIO = System.IO;

namespace FileUploadService.Application.Implementation;

public class FileService : IFileService
{
    private readonly FileStorageSettings _storageSettings;
    private readonly string _connectionString;
    private readonly ILogger<FileService> _logger;
    private readonly FileValidationService _validator;
    private readonly VirusScanService _virusScanner;
    private readonly EncryptionService _encryptionService;
    private readonly System.Data.IDbConnection? _testConnection; // injected in tests

    public FileService(
        IOptions<FileStorageSettings> storageSettings,
        IConfiguration configuration,
        ILogger<FileService> logger,
        FileValidationService validator,
        VirusScanService virusScanner,
        EncryptionService encryptionService)
    {
        _storageSettings = storageSettings.Value;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found.");
        _logger = logger;
        _validator = validator;
        _virusScanner = virusScanner;
        _encryptionService = encryptionService;
    }

    // Test-only constructor — accepts an injected IDbConnection (SQLite, etc.)
    internal FileService(
        IOptions<FileStorageSettings> storageSettings,
        IConfiguration configuration,
        ILogger<FileService> logger,
        FileValidationService validator,
        VirusScanService virusScanner,
        EncryptionService encryptionService,
        System.Data.IDbConnection testConnection)
        : this(storageSettings, configuration, logger, validator, virusScanner, encryptionService)
    {
        _testConnection = testConnection;
    }

    public async Task<FileUploadResponse> UploadFileAsync(IFormFile file, string? uploadedBy)
    {
        // ── STEP 1: VALIDATE ─────────────────────────────────
        var validationResult = await _validator.ValidateAsync(file);
        if (!validationResult.IsValid)
            throw new FileValidationException(validationResult);

        // ── STEP 2: VIRUS SCAN ───────────────────────────────
        var scanResult = await _virusScanner.ScanAsync(file);

        if (scanResult.ScannerUnavailable)
        {
            _logger.LogError(
                "Upload rejected — virus scanner unavailable. File: {Name}", file.FileName
            );
            throw new VirusScanException("Virus scanner is currently unavailable. Upload rejected for security. Please try again later.");
        }

        if (!scanResult.IsClean)
        {
            _logger.LogWarning(
                "Infected file blocked. Threat: {Threat}, File: {Name}",
                scanResult.ThreatName, file.FileName
            );
            throw new VirusDetectedException(scanResult.ThreatName ?? "Unknown threat");
        }

        // ── STEP 3: GENERATE IDs AND PATHS ───────────────────
        var referenceId = GenerateReferenceId();

        // Disk filename uses referenceId; .enc makes it clear it is encrypted
        var storedFilename = $"{referenceId}.enc";

        var now = DateTime.UtcNow;
        var relativeFolderPath = Path.Combine(
            _storageSettings.BasePath,
            now.Year.ToString(),
            now.Month.ToString("D2")
        );
        var absoluteFolderPath = Path.Combine(Directory.GetCurrentDirectory(), relativeFolderPath);
        var absoluteFilePath = Path.Combine(absoluteFolderPath, storedFilename);
        var relativeFilePath = Path.Combine(relativeFolderPath, storedFilename);

        Directory.CreateDirectory(absoluteFolderPath);

        // ── STEP 4: ENCRYPT ──────────────────────────────────
        _logger.LogInformation("Encrypting file: {Name}", file.FileName);

        await using var plaintextStream = file.OpenReadStream();
        var encryptionResult = await _encryptionService.EncryptAsync(plaintextStream);
        // ── STEP 5: WRITE ENCRYPTED FILE TO DISK ─────────────
        await SystemIO.File.WriteAllBytesAsync(absoluteFilePath, encryptionResult.EncryptedBytes);

        _logger.LogInformation(
            "Encrypted file written. ReferenceId: {Ref}, Path: {Path}",
            referenceId, relativeFilePath
        );

        // ── STEP 6: SAVE METADATA ────────────────────────────
        // Change 3: uploaded_by is now a microservice id (long), not a user UUID.
        // Parse as long; null if not provided or not a valid number.
        long? uploadedByLong = long.TryParse(uploadedBy, out var ms) ? ms : null;

        var metadata = new FileMetadata
        {
            // Change 2: Id is now BIGSERIAL — the DB assigns it; we leave it 0 here
            // and do not include it in the INSERT (let Postgres auto-increment).
            ReferenceId = referenceId,
            OriginalFilename = file.FileName,
            StoragePath = relativeFilePath,
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedBy = uploadedByLong,   // Change 3: long? FK
            CreatedAt = DateTime.UtcNow,
            Iv = encryptionResult.IvBase64,
            // Change 1: IsEncrypted removed — every file is always encrypted
        };

        await SaveMetadataAsync(metadata);

        return new FileUploadResponse
        {
            ReferenceId = metadata.ReferenceId,
            OriginalFilename = metadata.OriginalFilename,
            ContentType = metadata.ContentType,
            FileSizeBytes = metadata.FileSize,
            CreatedAt = metadata.CreatedAt
        };
    }

    // =========================================================
    //  DOWNLOAD — always decrypts (no legacy unencrypted path)
    // =========================================================
    public async Task<(FileMetadata Metadata, Stream FileStream)?> DownloadFileAsync(string referenceId)
    {
        var metadata = await GetMetadataByReferenceIdAsync(referenceId);

        if (metadata is null)
        {
            _logger.LogWarning("File not found. ReferenceId: {Ref}", referenceId);
            return null;
        }

        var absoluteFilePath = Path.IsPathRooted(metadata.StoragePath)
            ? metadata.StoragePath
            : Path.Combine(Directory.GetCurrentDirectory(), metadata.StoragePath);

        if (!SystemIO.File.Exists(absoluteFilePath))
        {
            _logger.LogError(
                "File in DB but missing on disk. ReferenceId: {Ref}, Path: {Path}",
                referenceId, absoluteFilePath
            );
            return null;
        }

        // Change 1: IsEncrypted flag is gone — all files are always encrypted.
        // Decrypt unconditionally. IV is guaranteed NOT NULL in the DB.
        if (string.IsNullOrWhiteSpace(metadata.Iv))
            throw new InvalidOperationException(
                $"File {referenceId} has no IV stored — data may be corrupt."
            );

        var encryptedBytes = await SystemIO.File.ReadAllBytesAsync(absoluteFilePath);
        await using var encryptedStream = new MemoryStream(encryptedBytes);

        var decryptedStream = await _encryptionService.DecryptAsync(
            encryptedStream,
            metadata.Iv
        );

        return (metadata, decryptedStream);
    }

    // =========================================================
    //  METADATA ONLY — DB query only, no file I/O, no decryption
    // =========================================================
    public async Task<FileMetadata?> GetFileMetadataAsync(string referenceId)
    {
        return await GetMetadataByReferenceIdAsync(referenceId);
    }

    // =========================================================
    //  PRIVATE HELPERS
    // =========================================================

    private static string GenerateReferenceId()
    {
        var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        var randomPart = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"FILE-{datePart}-{randomPart}";
    }

    private async Task SaveMetadataAsync(FileMetadata metadata)
    {
        System.Data.IDbConnection conn;
        NpgsqlConnection? ownedConnection = null;

        if (_testConnection is not null)
            conn = _testConnection;
        else
        {
            ownedConnection = new NpgsqlConnection(_connectionString);
            conn = ownedConnection;
        }

        try
        {
            // Change 1: is_encrypted removed from INSERT
            // Change 2: id not inserted — BIGSERIAL auto-assigns it
            // Change 3: uploaded_by is now a long FK
            const string sql = @"
                INSERT INTO files (
                    reference_id, original_filename, storage_path,
                    content_type, file_size, uploaded_by, created_at, iv, is_deleted
                ) VALUES (
                    @referenceId, @originalFilename, @storagePath,
                    @contentType, @fileSize, @uploadedBy, @createdAt, @iv, @isDeleted
                )";

            await conn.ExecuteAsync(sql, new
            {
                referenceId = metadata.ReferenceId,
                originalFilename = metadata.OriginalFilename,
                storagePath = metadata.StoragePath,
                contentType = metadata.ContentType,
                fileSize = metadata.FileSize,
                uploadedBy = metadata.UploadedBy,
                createdAt = metadata.CreatedAt,
                iv = metadata.Iv,
                isDeleted = metadata.IsDeleted
            });
        }
        finally { if (ownedConnection is not null) await ownedConnection.DisposeAsync(); }
    }

    public async Task<bool> DeleteFileAsync(string referenceId)
    {
        System.Data.IDbConnection conn;
        NpgsqlConnection? ownedConnection = null;

        if (_testConnection is not null)
            conn = _testConnection;
        else
        {
            ownedConnection = new NpgsqlConnection(_connectionString);
            conn = ownedConnection;
        }

        try
        {
            const string sql = @"
            UPDATE files
            SET    is_deleted = TRUE
            WHERE  reference_id = @referenceId
              AND  is_deleted   = FALSE";

            var affected = await conn.ExecuteAsync(sql, new { referenceId });

            if (affected == 0)
            {
                _logger.LogWarning(
                    "Soft-delete found no matching active file. ReferenceId: {Ref}", referenceId
                );
                return false;
            }

            _logger.LogInformation("File soft-deleted. ReferenceId: {Ref}", referenceId);
            return true;
        }
        finally { if (ownedConnection is not null) await ownedConnection.DisposeAsync(); }
    }

    private async Task<FileMetadata?> GetMetadataByReferenceIdAsync(string referenceId)
    {
        System.Data.IDbConnection conn;
        NpgsqlConnection? ownedConnection = null;

        if (_testConnection is not null)
            conn = _testConnection;
        else
        {
            ownedConnection = new NpgsqlConnection(_connectionString);
            conn = ownedConnection;
        }

        try
        {
            // Change 1: is_encrypted removed from SELECT
            // Change 2: id is now a long (BIGSERIAL), Dapper maps it automatically
            // Change 3: uploaded_by is now a long FK
            const string sql = @"
                SELECT
                    id                AS Id,
                    reference_id      AS ReferenceId,
                    original_filename AS OriginalFilename,
                    storage_path      AS StoragePath,
                    content_type      AS ContentType,
                    file_size         AS FileSize,
                    uploaded_by       AS UploadedBy,
                    created_at        AS CreatedAt,
                    iv                AS Iv,
                    is_deleted        AS IsDeleted
                FROM files
                WHERE reference_id = @referenceId
                AND is_deleted = FALSE";

            return await conn.QuerySingleOrDefaultAsync<FileMetadata>(sql, new { referenceId });
        }
        finally { if (ownedConnection is not null) await ownedConnection.DisposeAsync(); }
    }
}