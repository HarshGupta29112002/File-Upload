using FileUploadService.Application.DTOs;

namespace FileUploadService.Application.Interfaces;

/// <summary>
/// Abstracts all database operations for file metadata.
/// FileService never writes SQL — it calls this.
/// </summary>
public interface IFileRepository
{
    Task SaveAsync(FileMetadata metadata, CancellationToken ct = default);
    Task<FileMetadata?> GetByReferenceIdAsync(string referenceId, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(string referenceId, CancellationToken ct = default);
}