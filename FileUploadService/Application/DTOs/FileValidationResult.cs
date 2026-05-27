namespace FileUploadService.Application.DTOs;

public class FileValidationResult
{
    public bool IsValid { get; set; }

    public string? FailureReason { get; set; }

    public ValidationDetails Details { get; set; } = new();
}

public class ValidationDetails
{

    public string? ClaimedExtension { get; set; }

    public string? ClaimedMimeType { get; set; }

    public string? DetectedFileType { get; set; }

    public long FileSizeBytes { get; set; }
}