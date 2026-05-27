using FileUploadService.Application.Configurations;
using FileUploadService.Application.Implementation;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class EncryptionServiceTests
{
    private readonly Mock<ILogger<EncryptionService>> _loggerMock = new();

    private EncryptionService BuildService(byte[]? keyOverride = null)
    {
        var key = keyOverride ?? RandomNumberGenerator.GetBytes(32);
        var settings = Options.Create(new EncryptionSettings
        {
            AesKey = Convert.ToBase64String(key)
        });
        return new EncryptionService(settings, _loggerMock.Object);
    }

    // POSITIVE TEST CASES — EncryptAsync


    [Fact]
    public async Task EncryptAsync_AnyPlaintext_ReturnsNonEmptyEncryptedBytes()
    {
        
        var svc       = BuildService();
        var plaintext = "Hello Harsh, this is a secret!"u8.ToArray();
        using var stream = new MemoryStream(plaintext);

        
        var result = await svc.EncryptAsync(stream);

        
        result.EncryptedBytes.Should().NotBeEmpty();
    }


    [Fact]
    public async Task EncryptAsync_AnyPlaintext_ReturnsBase64IV()
    {
        
        var svc = BuildService();
        using var stream = new MemoryStream("test"u8.ToArray());

        
        var result = await svc.EncryptAsync(stream);

        
        result.IvBase64.Should().NotBeNullOrEmpty();
        var ivBytes = Convert.FromBase64String(result.IvBase64);
        ivBytes.Length.Should().Be(16, "AES-CBC IV is always 16 bytes");
    }


    [Fact]
    public async Task EncryptAsync_AnyPlaintext_EncryptedBytesDifferFromPlaintext()
    {
        
        var svc       = BuildService();
        var plaintext = "This must NOT appear in ciphertext!"u8.ToArray();
        using var stream = new MemoryStream(plaintext);

        
        var result = await svc.EncryptAsync(stream);

        
        result.EncryptedBytes.Should().NotEqual(plaintext);
    }


    [Fact]
    public async Task EncryptAsync_SamePlaintext_TwiceDifferentCiphertext()
    {
        
        var svc       = BuildService();
        var plaintext = "same data"u8.ToArray();

        using var stream1 = new MemoryStream(plaintext);
        using var stream2 = new MemoryStream(plaintext);

        
        var result1 = await svc.EncryptAsync(stream1);
        var result2 = await svc.EncryptAsync(stream2);

        
        result1.EncryptedBytes.Should().NotEqual(result2.EncryptedBytes,
            "each encryption uses a fresh random IV — same plaintext = different ciphertext");
        result1.IvBase64.Should().NotBe(result2.IvBase64);
    }

    // POSITIVE TEST CASES — DecryptAsync


    [Fact]
    public async Task DecryptAsync_ValidCiphertext_RecoveredOriginalData()
    {
        
        var key = RandomNumberGenerator.GetBytes(32);
        var svc = BuildService(key);

        var original = "Harsh ka secret data"u8.ToArray();
        using var plainStream = new MemoryStream(original);
        var encrypted = await svc.EncryptAsync(plainStream);

        using var encStream = new MemoryStream(encrypted.EncryptedBytes);

        
        var decryptedStream = await svc.DecryptAsync(encStream, encrypted.IvBase64);

        
        var decryptedBytes = ((MemoryStream)decryptedStream).ToArray();
        decryptedBytes.Should().Equal(original);
    }


    [Fact]
    public async Task DecryptAsync_ValidCiphertext_StreamPositionIsZero()
    {
        var svc = BuildService();
        using var plainStream = new MemoryStream("position test"u8.ToArray());
        var encrypted = await svc.EncryptAsync(plainStream);
        using var encStream = new MemoryStream(encrypted.EncryptedBytes);

        var decrypted = await svc.DecryptAsync(encStream, encrypted.IvBase64);

        decrypted.Position.Should().Be(0);
    }

    [Fact]
    public async Task RoundTrip_EncryptThenDecrypt_ProducesIdenticalBytes()
    {
        var key     = RandomNumberGenerator.GetBytes(32);
        var svc     = BuildService(key);
        var payload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        using var encryptInput = new MemoryStream(payload);
        var encrypted = await svc.EncryptAsync(encryptInput);

        using var decryptInput = new MemoryStream(encrypted.EncryptedBytes);

        var decryptedStream = await svc.DecryptAsync(decryptInput, encrypted.IvBase64);
        var result = ((MemoryStream)decryptedStream).ToArray();

        result.Should().Equal(payload);
    }

    // NEGATIVE TEST CASES

    [Fact]
    public async Task DecryptAsync_InvalidBase64IV_ThrowsInvalidOperationException()
    {
        var svc = BuildService();
        using var encStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        var act = async () => await svc.DecryptAsync(encStream, "NOT_VALID_BASE64!!!");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*not valid Base64*");
    }

    [Fact]
    public async Task DecryptAsync_WrongIVLength_ThrowsInvalidOperationException()
    {
        // Arrange
        var svc   = BuildService();
        var wrongIv = Convert.ToBase64String(new byte[8]); // only 8 bytes
        using var encStream = new MemoryStream(new byte[] { 1, 2, 3, 4 });

        // Act
        var act = async () => await svc.DecryptAsync(encStream, wrongIv);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IV must be 16 bytes*");
    }

    // ── Wrong key used for decryption → cryptographic error ───────

    [Fact]
    public async Task DecryptAsync_WrongKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var correctKey = RandomNumberGenerator.GetBytes(32);
        var wrongKey   = RandomNumberGenerator.GetBytes(32);

        var svcEncrypt = BuildService(correctKey);
        var svcDecrypt = BuildService(wrongKey);

        using var plainStream = new MemoryStream("sensitive data"u8.ToArray());
        var encrypted = await svcEncrypt.EncryptAsync(plainStream);

        using var encStream = new MemoryStream(encrypted.EncryptedBytes);

        // Act
        var act = async () => await svcDecrypt.DecryptAsync(encStream, encrypted.IvBase64);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to decrypt*");
    }

    // ── Misconfigured key (not 32 bytes) → startup exception ──────

    [Fact]
    public void Constructor_InvalidKeyLength_ThrowsInvalidOperationException()
    {
        // Arrange — only 16 bytes (AES-128, not AES-256)
        var shortKey = RandomNumberGenerator.GetBytes(16);
        var settings = Options.Create(new EncryptionSettings
        {
            AesKey = Convert.ToBase64String(shortKey)
        });

        // Act
        var act = () => new EncryptionService(settings, _loggerMock.Object);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*");
    }
}
