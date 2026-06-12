using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;

namespace FileUploadService.Application.Implementation;

/// <summary>
/// Orchestrates upload and download.
/// Has zero knowledge of SQL or disk paths — delegates entirely to
/// IFileRepository (DB) and IStorageService (disk/MinIO).
/// </summary>
public class FileService : IFileService
{
    private readonly IStorageService _storage;
    private readonly IFileRepository _repository;
    private readonly ILogger<FileService> _logger;
    private readonly FileValidationService _validator;
    private readonly VirusScanService _virusScanner;

    public FileService(
        IStorageService storage,
        IFileRepository repository,
        ILogger<FileService> logger,
        FileValidationService validator,
        VirusScanService virusScanner)
    {
        _storage = storage;
        _repository = repository;
        _logger = logger;
        _validator = validator;
        _virusScanner = virusScanner;
    }

    // =========================================================
    //  UPLOAD
    //  Validate → Virus scan → Stream to storage → Save metadata
    // =========================================================
    public async Task<FileUploadResponse> UploadFileAsync(IFormFile file, string? uploadedBy)
    {
        // 1. Validate (MIME + size)
        var validationResult = await _validator.ValidateAsync(file);
        if (!validationResult.IsValid)
            throw new FileValidationException(validationResult);

        // 2. Virus scan
        var scanResult = await _virusScanner.ScanAsync(file);

        if (scanResult.ScannerUnavailable)
            throw new VirusScanException(
                "Virus scanner is currently unavailable. Upload rejected for security. Please try again later."
            );

        if (!scanResult.IsClean)
            throw new VirusDetectedException(scanResult.ThreatName ?? "Unknown threat");

        // 3. Build reference ID and filename
        var referenceId = GenerateReferenceId();
        var storedFilename = $"{referenceId}.bin"; // plain binary — encryption removed

        // 4. Stream directly to storage (no RAM buffering)
        _logger.LogInformation("Saving file. Name: {Name}, Size: {Size}", file.FileName, file.Length);

        await using var readStream = file.OpenReadStream();
        var storagePath = await _storage.SaveAsync(readStream, storedFilename);

        _logger.LogInformation(
            "File saved. ReferenceId: {Ref}, Path: {Path}", referenceId, storagePath
        );

        // 5. Save metadata to DB
        long? uploadedByLong = long.TryParse(uploadedBy, out var parsed) ? parsed : null;

        var metadata = new FileMetadata
        {
            ReferenceId = referenceId,
            OriginalFilename = file.FileName,
            StoragePath = storagePath,
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedBy = uploadedByLong,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.SaveAsync(metadata);

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
    //  DOWNLOAD
    //  Fetch metadata → Open storage stream → Return to caller
    // =========================================================
    public async Task<(FileMetadata Metadata, Stream FileStream)?> DownloadFileAsync(string referenceId)
    {
        var metadata = await _repository.GetByReferenceIdAsync(referenceId);

        if (metadata is null)
        {
            _logger.LogWarning("File not found. ReferenceId: {Ref}", referenceId);
            return null;
        }

        var stream = await _storage.ReadAsync(metadata.StoragePath);

        if (stream is null)
        {
            _logger.LogError(
                "File in DB but missing in storage. ReferenceId: {Ref}, Path: {Path}",
                referenceId, metadata.StoragePath
            );
            return null;
        }

        return (metadata, stream);
    }

    // =========================================================
    //  METADATA ONLY — DB query, no file I/O
    // =========================================================
    public async Task<FileMetadata?> GetFileMetadataAsync(string referenceId) =>
        await _repository.GetByReferenceIdAsync(referenceId);

    // =========================================================
    //  SOFT DELETE
    // =========================================================
    public async Task<bool> DeleteFileAsync(string referenceId)
    {
        var deleted = await _repository.SoftDeleteAsync(referenceId);

        if (!deleted)
            _logger.LogWarning(
                "Soft-delete found no matching active file. ReferenceId: {Ref}", referenceId
            );
        else
            _logger.LogInformation("File soft-deleted. ReferenceId: {Ref}", referenceId);

        return deleted;
    }

    // =========================================================
    //  PRIVATE
    // =========================================================
    private static string GenerateReferenceId()
    {
        var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        var randomPart = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        return $"FILE-{datePart}-{randomPart}";
    }
}