namespace FileUploadService.Application.Interfaces;

/// <summary>
/// Abstracts file storage I/O.
/// Current implementation: LocalStorageService (disk).
/// Future swap: MinioStorageService — change one registration in Program.cs, nothing else.
/// </summary>
public interface IStorageService
{
    /// <summary>
    /// Streams the source directly to storage. Returns the relative storage path.
    /// </summary>
    Task<string> SaveAsync(Stream source, string filename, CancellationToken ct = default);


    // Overload for custom base path — used by VideoService
    Task<string> SaveAsync(Stream source, string filename, string basePath, CancellationToken ct = default);

    /// <summary>
    /// Returns a readable stream for the file at the given storage path.
    /// Returns null if the file does not exist.
    /// </summary>
    Task<Stream?> ReadAsync(string storagePath, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes the file from storage.
    /// </summary>
    Task DeleteAsync(string storagePath, CancellationToken ct = default);
}