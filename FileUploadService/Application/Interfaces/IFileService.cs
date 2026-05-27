using FileUploadService.Application.DTOs;

namespace FileUploadService.Application.Interfaces;

public interface IFileService
{
    Task<FileUploadResponse> UploadFileAsync(IFormFile file, string? uploadedBy);

    Task<(FileMetadata Metadata, Stream FileStream)?> DownloadFileAsync(string referenceId);

    // BUG FIX: added dedicated metadata-only lookup so the metadata endpoint
    // does not have to decrypt the entire file just to read its DB record.
    Task<FileMetadata?> GetFileMetadataAsync(string referenceId);

    Task<bool> DeleteFileAsync(string referenceId);
}