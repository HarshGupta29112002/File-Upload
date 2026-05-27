namespace FileUploadService.Application.DTOs;

public class FileUploadResponse
{
   
    public string ReferenceId { get; set; } = string.Empty;

    public string OriginalFilename { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public long FileSizeBytes { get; set; }

    public DateTime CreatedAt { get; set; }
}