namespace FileUploadService.Application.DTOs;

public class FileValidationException : Exception
{
    public FileValidationResult ValidationResult { get; }

    public FileValidationException(FileValidationResult result)
        : base(result.FailureReason ?? "File validation failed.")
    {
        ValidationResult = result;
    }
}