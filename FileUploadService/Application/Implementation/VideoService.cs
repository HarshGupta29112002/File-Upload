using Dapper;
using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FileUploadService.Application.Implementation;

/// <summary>
/// Orchestrates video upload and download.
/// Flow: Validate → Virus scan → Save to disk → FFprobe → Save metadata → Return response
/// No video bytes are held in RAM beyond what streams through the pipeline.
/// </summary>
public class VideoService : IVideoService
{
    private readonly IStorageService _storage;
    private readonly IFileRepository _repository;
    private readonly VideoValidationService _validator;
    private readonly VirusScanService _virusScanner;
    private readonly FfprobeService _ffprobe;
    private readonly VideoStorageSettings _videoSettings;
    private readonly string _connectionString;
    private readonly ILogger<VideoService> _logger;

    private const int StreamBufferSize = 81_920; // 80 KB

    public VideoService(
        IStorageService storage,
        IFileRepository repository,
        VideoValidationService validator,
        VirusScanService virusScanner,
        FfprobeService ffprobe,
        IOptions<VideoStorageSettings> videoSettings,
        IConfiguration configuration,
        ILogger<VideoService> logger)
    {
        _storage = storage;
        _repository = repository;
        _validator = validator;
        _virusScanner = virusScanner;
        _ffprobe = ffprobe;
        _videoSettings = videoSettings.Value;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found.");
        _logger = logger;
    }

    // =========================================================
    //  UPLOAD
    //  Validate → Virus scan → Save to disk → FFprobe → Save metadata
    // =========================================================
    public async Task<VideoUploadResponse> UploadVideoAsync(IFormFile file, string? uploadedBy)
    {
        // 1. Validate (MIME + magic bytes + size)
        var validation = await _validator.ValidateAsync(file);
        if (!validation.IsValid)
            throw new FileValidationException(validation);

        // 2. Virus scan (reuse existing)
        var scan = await _virusScanner.ScanAsync(file);
        if (scan.ScannerUnavailable)
            throw new VirusScanException(
                "Virus scanner is currently unavailable. Upload rejected for security."
            );
        if (!scan.IsClean)
            throw new VirusDetectedException(scan.ThreatName ?? "Unknown threat");

        // 3. Generate IDs and stream to disk
        var referenceId = GenerateReferenceId();
        var storedFilename = $"{referenceId}.mp4";

        _logger.LogInformation(
            "Saving video. Filename: {Name}, Size: {Size} bytes", file.FileName, file.Length
        );

        // LocalStorageService.SaveAsync uses date-partitioned folders: videos/2026/06/FILE-xxx.mp4
        await using var readStream = file.OpenReadStream();
        var storagePath = await _storage.SaveAsync(readStream, storedFilename, _videoSettings.BasePath);

        _logger.LogInformation(
            "Video saved to disk. ReferenceId: {Ref}, Path: {Path}", referenceId, storagePath
        );

        // 4. Run FFprobe on the saved file (reads from disk — not from RAM)
        var absolutePath = Path.IsPathRooted(storagePath)
            ? storagePath
            : Path.Combine(Directory.GetCurrentDirectory(), storagePath);

        var probe = await _ffprobe.ExtractAsync(absolutePath);

        if (!probe.Success)
            _logger.LogWarning(
                "FFprobe metadata extraction failed for {Ref}: {Err}",
                referenceId, probe.ErrorMessage
            );
        else
            _logger.LogInformation(
                "Video metadata extracted. ReferenceId: {Ref}, Duration: {Dur}s, Resolution: {W}x{H}, Codec: {C}",
                referenceId, probe.DurationSeconds, probe.Width, probe.Height, probe.VideoCodec
            );

        // 5. Save file record to files table (reuse existing repository)
        long? uploadedByLong = long.TryParse(uploadedBy, out var parsed) ? parsed : null;

        var fileMeta = new FileMetadata
        {
            ReferenceId = referenceId,
            OriginalFilename = file.FileName,
            StoragePath = storagePath,
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedBy = uploadedByLong,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.SaveAsync(fileMeta);

        // 6. Save video metadata to video_metadata table
        var videoMeta = new VideoMetadata
        {
            FileId = fileMeta.Id,
            ReferenceId = referenceId,
            DurationSeconds = probe.DurationSeconds,
            Width = probe.Width,
            Height = probe.Height,
            VideoCodec = probe.VideoCodec,
            AudioCodec = probe.AudioCodec,
            FrameRate = probe.FrameRate,
            BitRate = probe.BitRate,
            CreatedAt = DateTime.UtcNow
        };

        await SaveVideoMetadataAsync(videoMeta);

        return new VideoUploadResponse
        {
            ReferenceId = referenceId,
            OriginalFilename = file.FileName,
            ContentType = file.ContentType ?? "video/mp4",
            FileSizeBytes = file.Length,
            CreatedAt = fileMeta.CreatedAt,
            DurationSeconds = probe.DurationSeconds,
            Resolution = (probe.Width.HasValue && probe.Height.HasValue)
                                   ? $"{probe.Width}x{probe.Height}"
                                   : null,
            VideoCodec = probe.VideoCodec,
            AudioCodec = probe.AudioCodec,
            FrameRate = probe.FrameRate,
            BitRate = probe.BitRate
        };
    }

    // =========================================================
    //  DOWNLOAD — returns a stream for range-request-capable streaming
    // =========================================================
    public async Task<(FileMetadata Metadata, Stream VideoStream)?> DownloadVideoAsync(string referenceId)
    {
        var metadata = await _repository.GetByReferenceIdAsync(referenceId);

        if (metadata is null)
        {
            _logger.LogWarning("Video not found. ReferenceId: {Ref}", referenceId);
            return null;
        }

        var stream = await _storage.ReadAsync(metadata.StoragePath);

        if (stream is null)
        {
            _logger.LogError(
                "Video in DB but missing on disk. ReferenceId: {Ref}, Path: {Path}",
                referenceId, metadata.StoragePath
            );
            return null;
        }

        return (metadata, stream);
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

    private async Task SaveVideoMetadataAsync(VideoMetadata meta)
    {
        const string sql = @"
            INSERT INTO video_metadata (
                file_id, reference_id, duration_seconds, width, height,
                video_codec, audio_codec, frame_rate, bit_rate, created_at
            ) VALUES (
                @fileId, @referenceId, @durationSeconds, @width, @height,
                @videoCodec, @audioCodec, @frameRate, @bitRate, @createdAt
            )";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.ExecuteAsync(sql, new
        {
            fileId = meta.FileId,
            referenceId = meta.ReferenceId,
            durationSeconds = meta.DurationSeconds,
            width = meta.Width,
            height = meta.Height,
            videoCodec = meta.VideoCodec,
            audioCodec = meta.AudioCodec,
            frameRate = meta.FrameRate,
            bitRate = meta.BitRate,
            createdAt = meta.CreatedAt
        });
    }
}