using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using Microsoft.Extensions.Options;

namespace FileUploadService.Application.Implementation;

/// <summary>
/// Validates video files before storage.
/// Two checks:
///   1. File size — against configured max (no file read needed, uses header length)
///   2. MIME type — magic bytes check on the first 16 bytes only (not a full RAM load)
/// </summary>
public class VideoValidationService
{
    private readonly VideoStorageSettings _settings;
    private readonly ILogger<VideoValidationService> _logger;

    public VideoValidationService(
        IOptions<VideoStorageSettings> settings,
        ILogger<VideoValidationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<FileValidationResult> ValidateAsync(IFormFile file)
    {
        // Size check
        if (file.Length == 0)
            return FileValidationResult.Fail("Video file is empty.");

        if (file.Length > _settings.MaxFileSizeBytes)
        {
            var maxMb = _settings.MaxFileSizeBytes / 1_048_576;
            var fileMb = file.Length / 1_048_576;
            return FileValidationResult.Fail(
                $"Video size ({fileMb} MB) exceeds the maximum allowed ({maxMb} MB)."
            );
        }

        // Extension check
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!_settings.AllowedExtensions.Contains(ext))
            return FileValidationResult.Fail(
                $"Extension '{ext}' is not allowed. Allowed: {string.Join(", ", _settings.AllowedExtensions)}"
            );

        // MIME type check against config
        if (!_settings.AllowedMimeTypes.TryGetValue(ext, out var expectedMime) ||
            !string.Equals(file.ContentType, expectedMime, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Video rejected — MIME mismatch. Extension: {Ext}, ContentType: {CT}",
                ext, file.ContentType
            );
            return FileValidationResult.Fail(
                $"Content type '{file.ContentType}' does not match expected type for '{ext}'."
            );
        }

        // Magic bytes check (first 8 bytes only)
        await using var stream = file.OpenReadStream();
        var header = new byte[16];
        var bytesRead = await stream.ReadAsync(header.AsMemory(0, header.Length));

        if (bytesRead < 8)
            return FileValidationResult.Fail("Video file is too small to be valid.");

        if (!HasFtypBox(header))
        {
            _logger.LogWarning(
                "Video rejected — magic bytes invalid. Filename: {Name}", file.FileName
            );
            return FileValidationResult.Fail(
                "File content does not match a valid MP4 or MOV container."
            );
        }

        return FileValidationResult.Ok();
    }

    private static bool HasFtypBox(byte[] header) =>
        header.Length >= 8 &&
        header[4] == 0x66 &&
        header[5] == 0x74 &&
        header[6] == 0x79 &&
        header[7] == 0x70;
}