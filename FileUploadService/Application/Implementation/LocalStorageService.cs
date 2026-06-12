using FileUploadService.Application.Configurations;
using FileUploadService.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace FileUploadService.Application.Implementation;

/// <summary>
/// Stores files on local disk using streaming FileStream.
/// No file is ever fully loaded into RAM.
///
/// SaveAsync has an overload that accepts a custom basePath —
/// VideoService uses "uploads/videos", FileService uses "uploads".
/// Same implementation, different destination folder.
/// </summary>
public class LocalStorageService : IStorageService
{
    private readonly string _defaultBasePath;
    private readonly ILogger<LocalStorageService> _logger;

    private const int BufferSize = 81_920; // 80 KB

    public LocalStorageService(
        IOptions<FileStorageSettings> settings,
        ILogger<LocalStorageService> logger)
    {
        _defaultBasePath = settings.Value.BasePath;
        _logger = logger;
    }

    // IStorageService implementation — uses default base path
    public Task<string> SaveAsync(Stream source, string filename, CancellationToken ct = default)
        => SaveAsync(source, filename, _defaultBasePath, ct);

    // Overload used by VideoService — accepts a custom base path
    public async Task<string> SaveAsync(
        Stream source,
        string filename,
        string basePath,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var relativeFolder = Path.Combine(
            basePath,
            now.Year.ToString(),
            now.Month.ToString("D2")
        );

        var absoluteFolder = Path.Combine(Directory.GetCurrentDirectory(), relativeFolder);
        Directory.CreateDirectory(absoluteFolder);

        var relativeFilePath = Path.Combine(relativeFolder, filename);
        var absoluteFilePath = Path.Combine(Directory.GetCurrentDirectory(), relativeFilePath);

        await using var fileStream = new FileStream(
            absoluteFilePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: BufferSize,
            useAsync: true
        );

        await source.CopyToAsync(fileStream, BufferSize, ct);

        _logger.LogInformation(
            "File saved. Path: {Path}, Size: {Size} bytes",
            relativeFilePath, fileStream.Position
        );

        return relativeFilePath;
    }

    public Task<Stream?> ReadAsync(string storagePath, CancellationToken ct = default)
    {
        var absolutePath = ToAbsolute(storagePath);

        if (!File.Exists(absolutePath))
        {
            _logger.LogWarning("File not found on disk. Path: {Path}", absolutePath);
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: BufferSize,
            useAsync: true
        );

        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct = default)
    {
        var absolutePath = ToAbsolute(storagePath);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
            _logger.LogInformation("File deleted. Path: {Path}", absolutePath);
        }

        return Task.CompletedTask;
    }

    private static string ToAbsolute(string path) =>
        Path.IsPathRooted(path)
            ? path
            : Path.Combine(Directory.GetCurrentDirectory(), path);
}