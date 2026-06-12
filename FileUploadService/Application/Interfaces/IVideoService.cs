using FileUploadService.Application.DTOs;

namespace FileUploadService.Application.Interfaces;

public interface IVideoService
{
    Task<VideoUploadResponse> UploadVideoAsync(IFormFile file, string? uploadedBy);
    Task<(FileMetadata Metadata, Stream VideoStream)?> DownloadVideoAsync(string referenceId);
}