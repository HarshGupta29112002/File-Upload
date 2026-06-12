namespace FileUploadService.Application.DTOs;

/// <summary>
/// DB entity — maps to the video_metadata table.
/// </summary>
public class VideoMetadata
{
    public long Id { get; set; }
    public long FileId { get; set; }   // FK → files.id
    public string ReferenceId { get; set; } = string.Empty;
    public decimal? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public string? FrameRate { get; set; }
    public long? BitRate { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Response DTO — returned to callers after a successful video upload.
/// </summary>
public class VideoUploadResponse
{
    public string ReferenceId { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }

    // Video-specific fields
    public decimal? DurationSeconds { get; set; }
    public string? Resolution { get; set; }   // "1920x1080"
    public string? VideoCodec { get; set; }   // "h264"
    public string? AudioCodec { get; set; }   // "aac"
    public string? FrameRate { get; set; }   // "30/1"
    public long? BitRate { get; set; }
}

/// <summary>
/// Internal DTO — result of running FFprobe on a saved video file.
/// </summary>
public class FfprobeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public decimal? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public string? FrameRate { get; set; }
    public long? BitRate { get; set; }
}