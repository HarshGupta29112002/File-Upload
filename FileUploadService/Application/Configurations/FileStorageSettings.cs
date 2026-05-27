namespace FileUploadService.Application.Configurations;

public class FileStorageSettings
{
    public const string SectionName = "FileStorage";

    public string BasePath { get; set; } = string.Empty;

    public long MaxFileSizeBytes { get; set; }

    public List<string> AllowedExtensions { get; set; } = new();

    public Dictionary<string, string> AllowedMimeTypes { get; set; } = new();
}

public class ClamAvSettings
{
    public const string SectionName = "ClamAV";
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 120;
}

public class EncryptionSettings
{
    public const string SectionName = "Encryption";

    // Change 4: AesKey MUST come from appsettings.json (or environment variable
    // override). It is never hardcoded here. If it is missing or wrong length,
    // GetKeyBytes() throws at startup so the app refuses to start rather than
    // silently using a bad key.
    //
    // In appsettings.json:
    //   "Encryption": {
    //     "AesKey": "<base64-encoded 32-byte key>"
    //   }
    //
    // Generate a fresh key:
    //   Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
    public string AesKey { get; set; } = string.Empty;

    public byte[] GetKeyBytes()
    {
        // Change 4: explicit, clear error if AesKey is absent from config
        if (string.IsNullOrWhiteSpace(AesKey))
            throw new InvalidOperationException(
                "Encryption:AesKey is not set in configuration. " +
                "Add it to appsettings.json under the 'Encryption' section. " +
                "Generate a key with: " +
                "Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))"
            );

        byte[] keyBytes;
        try
        {
            keyBytes = Convert.FromBase64String(AesKey);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Encryption:AesKey in configuration is not valid Base64.", ex
            );
        }

        if (keyBytes.Length != 32)
            throw new InvalidOperationException(
                $"Encryption:AesKey must decode to exactly 32 bytes (256 bits). " +
                $"Got {keyBytes.Length} bytes. " +
                "Generate a valid key with: " +
                "Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))"
            );

        return keyBytes;
    }
}