namespace FileUploadService.Application.DTOs;

public class EncryptionResult
{
    public byte[] EncryptedBytes { get; set; } = Array.Empty<byte>();

    public string IvBase64 { get; set; } = string.Empty;
}