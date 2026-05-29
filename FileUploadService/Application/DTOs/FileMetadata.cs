namespace FileUploadService.Application.DTOs;

public class FileMetadata
{
    // Change 2: was Guid, now long to match BIGSERIAL (1, 2, 3 ...)
    public long Id { get; set; }

    public string ReferenceId { get; set; } = string.Empty;
    public string OriginalFilename { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }

    // Change 3: was Guid? (bare UUID), now long? — FK into microservices table
    public long? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    // IV is always present — every stored file is encrypted
    public string Iv { get; set; } = null;


    public bool IsDeleted { get; set; } = false;
    // Change 1: IsEncrypted removed — all files are always encrypted.
    // The presence of a non-empty IV is the implicit guarantee.
    // public bool IsEncrypted { get; set; }  ← REMOVED
}