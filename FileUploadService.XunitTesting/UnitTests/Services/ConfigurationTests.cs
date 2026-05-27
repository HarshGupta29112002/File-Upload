using FileUploadService.Application.Configurations;
using FluentAssertions;
using System.Security.Cryptography;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class ConfigurationTests
{
    // FileStorageSettings

    [Fact]
    public void FileStorageSettings_SectionName_IsFileStorage()
    {
        FileStorageSettings.SectionName.Should().Be("FileStorage");
    }

    [Fact]
    public void FileStorageSettings_Defaults_AreEmpty()
    {
        var s = new FileStorageSettings();
        s.BasePath.Should().BeEmpty();
        s.MaxFileSizeBytes.Should().Be(0);
        s.AllowedExtensions.Should().BeEmpty();
        s.AllowedMimeTypes.Should().BeEmpty();
    }

    [Fact]
    public void FileStorageSettings_AllPropertiesSetCorrectly()
    {
        var s = new FileStorageSettings
        {
            BasePath = "uploads",
            MaxFileSizeBytes = 10_485_760,
            AllowedExtensions = new List<string> { ".jpg", ".pdf" },
            AllowedMimeTypes = new Dictionary<string, string> { { ".jpg", "image/jpeg" } }
        };

        s.BasePath.Should().Be("uploads");
        s.MaxFileSizeBytes.Should().Be(10_485_760);
        s.AllowedExtensions.Should().Contain(".jpg");
        s.AllowedMimeTypes[".jpg"].Should().Be("image/jpeg");
    }

    // ClamAvSettings

    [Fact]
    public void ClamAvSettings_SectionName_IsClamAV()
    {
        ClamAvSettings.SectionName.Should().Be("ClamAV");
    }

    [Fact]
    public void ClamAvSettings_DefaultHost_IsLocalhost()
    {
        var s = new ClamAvSettings();
        s.Host.Should().Be("127.0.0.1");
        s.Port.Should().Be(3310);
        s.TimeoutSeconds.Should().Be(120);
    }

    [Fact]
    public void ClamAvSettings_AllPropertiesSetCorrectly()
    {
        var s = new ClamAvSettings
        {
            Host = "clamav.internal",
            Port = 3311,
            TimeoutSeconds = 60
        };

        s.Host.Should().Be("clamav.internal");
        s.Port.Should().Be(3311);
        s.TimeoutSeconds.Should().Be(60);
    }

    // EncryptionSettings — contains real logic in GetKeyBytes()

    [Fact]
    public void EncryptionSettings_SectionName_IsEncryption()
    {
        EncryptionSettings.SectionName.Should().Be("Encryption");
    }

    [Fact]
    public void EncryptionSettings_DefaultAesKey_IsEmpty()
    {
        var s = new EncryptionSettings();
        s.AesKey.Should().BeEmpty();
    }

    [Fact]
    public void GetKeyBytes_ValidKey_ReturnsRawBytes()
    {
        var rawKey = RandomNumberGenerator.GetBytes(32);
        var s = new EncryptionSettings { AesKey = Convert.ToBase64String(rawKey) };

        var result = s.GetKeyBytes();
        result.Should().Equal(rawKey);
        result.Length.Should().Be(32);
    }

    [Fact]
    public void GetKeyBytes_EmptyKey_ThrowsInvalidOperationException()
    {
        var s = new EncryptionSettings { AesKey = "" };

        var act = () => s.GetKeyBytes();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not configured*");
    }

    [Fact]
    public void GetKeyBytes_NullKey_ThrowsInvalidOperationException()
    {
        var s = new EncryptionSettings { AesKey = null! };

        var act = () => s.GetKeyBytes();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetKeyBytes_16ByteKey_ThrowsInvalidOperationException()
    {
        // AES-128 key (16 bytes) — not valid for AES-256
        var shortKey = RandomNumberGenerator.GetBytes(16);
        var s = new EncryptionSettings { AesKey = Convert.ToBase64String(shortKey) };

        var act = () => s.GetKeyBytes();
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*32 bytes*");
    }

    [Fact]
    public void GetKeyBytes_WhitespaceKey_ThrowsInvalidOperationException()
    {
        var s = new EncryptionSettings { AesKey = "   " };

        var act = () => s.GetKeyBytes();
        act.Should().Throw<InvalidOperationException>();
    }
}