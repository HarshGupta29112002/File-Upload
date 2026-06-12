namespace FileUploadService.Application.Configurations;

public class VideoStorageSettings
{
    public const string SectionName = "VideoStorage";

    /// <summary>Base folder for video files, relative to app root. e.g. "uploads/videos"</summary>
    public string BasePath { get; set; } = "uploads/videos";

    /// <summary>Maximum allowed video file size in bytes. Default: 500 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 524_288_000; // 500 MB

    public List<string> AllowedExtensions { get; set; } = new();

    public Dictionary<string, string> AllowedMimeTypes { get; set; } = new();
}