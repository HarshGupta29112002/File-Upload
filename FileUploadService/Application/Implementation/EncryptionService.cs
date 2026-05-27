using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;

namespace FileUploadService.Application.Implementation;

public class EncryptionService
{
    private readonly byte[] _keyBytes;
    private readonly ILogger<EncryptionService> _logger;

    public EncryptionService(
        IOptions<EncryptionSettings> settings,
        ILogger<EncryptionService> logger)
    {

        _keyBytes = settings.Value.GetKeyBytes();
        _logger = logger;
    }

    //  ENCRYPT
    public async Task<EncryptionResult> EncryptAsync(Stream plaintextStream)
    {
        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _keyBytes;
        aes.GenerateIV();
        var iv = aes.IV;

         var encryptedStream = new MemoryStream();

        using (var encryptor = aes.CreateEncryptor())
        {
            await using var cryptoStream = new CryptoStream(
                encryptedStream,
                encryptor,
                CryptoStreamMode.Write,
                leaveOpen: true
            );

            await plaintextStream.CopyToAsync(cryptoStream);
            await cryptoStream.FlushFinalBlockAsync();
        }

        _logger.LogInformation(
            "File encrypted. Plaintext: {PlainSize} bytes, Encrypted: {EncSize} bytes",
            plaintextStream.Length, encryptedStream.Length
        );

        return new EncryptionResult
        {
            EncryptedBytes = encryptedStream.ToArray(),
            IvBase64 = Convert.ToBase64String(iv)
        };
    }

    //  DECRYPT
    public async Task<Stream> DecryptAsync(Stream encryptedStream, string ivBase64)
    {
        byte[] iv;
        try { iv = Convert.FromBase64String(ivBase64); }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Stored IV is not valid Base64.", ex);
        }

        if (iv.Length != 16)
            throw new InvalidOperationException($"IV must be 16 bytes. Got {iv.Length}.");

        using var aes = Aes.Create();
        aes.KeySize = 256;
        aes.BlockSize = 128;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = _keyBytes;
        aes.IV = iv;

        var encryptedSize = encryptedStream.Length;

        var decryptedOutput = new MemoryStream();

        try
        {
            using var decryptor = aes.CreateDecryptor();

            await using var cryptoStream = new CryptoStream(
                encryptedStream,
                decryptor,
                CryptoStreamMode.Read,
                leaveOpen: true 
            );

            await cryptoStream.CopyToAsync(decryptedOutput);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                "Failed to decrypt file. File may be corrupted or key may have changed.", ex);
        }

        decryptedOutput.Position = 0;

        _logger.LogInformation(
            "File decrypted. Encrypted: {EncSize} bytes, Decrypted: {DecSize} bytes",
            encryptedSize,
            decryptedOutput.Length
        );

        return decryptedOutput;
    }
}