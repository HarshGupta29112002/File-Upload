namespace FileUploadService.Application.DTOs;

public class FileUploadRequest
{

    public IFormFile File { get; set; } = null!;

    public string? UploadedBy { get; set; }
}