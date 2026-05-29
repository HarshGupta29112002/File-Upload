using FileUploadService.Application.Configurations;
using FluentAssertions;
using System.Security.Cryptography;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class ConfigurationTests
{
    // =========================================================
    // FileStorageSettings
    // =========================================================

    [Fact]
    public void FileStorageSettings_SectionName_IsFileStorage()
        => FileStorageSettings.SectionName.Should().Be("FileStorage");

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

    // =========================================================
    // ClamAvSettings
    // =========================================================

    [Fact]
    public void ClamAvSettings_SectionName_IsClamAV()
        => ClamAvSettings.SectionName.Should().Be("ClamAV");

    [Fact]
    public void ClamAvSettings_DefaultValues_AreCorrect()
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

    // =========================================================
    // EncryptionSettings
    // =========================================================

    [Fact]
    public void EncryptionSettings_SectionName_IsEncryption()
        => EncryptionSettings.SectionName.Should().Be("Encryption");

    [Fact]
    public void EncryptionSettings_DefaultAesKey_IsEmpty()
        => new EncryptionSettings().AesKey.Should().BeEmpty();

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

        act.Should().Throw<InvalidOperationException>();
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
        var shortKey = RandomNumberGenerator.GetBytes(16);
        var s = new EncryptionSettings { AesKey = Convert.ToBase64String(shortKey) };
        var act = () => s.GetKeyBytes();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*32 bytes*");
    }

    [Fact]
    public void GetKeyBytes_InvalidBase64_ThrowsInvalidOperationException()
    {
        var s = new EncryptionSettings { AesKey = "NOT_VALID_BASE64!!!" };
        var act = () => s.GetKeyBytes();

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Base64*");
    }
}

// =========================================================
// FileMetadata — DTO defaults and property tests
// =========================================================
public class FileMetadataTests
{
    [Fact]
    public void FileMetadata_DefaultValues_AreCorrect()
    {
        var m = new FileUploadService.Application.DTOs.FileMetadata();
        m.IsDeleted.Should().BeFalse();
        m.Iv.Should().BeNull();
        m.UploadedBy.Should().BeNull();
        m.StoragePath.Should().BeNullOrEmpty();
    }

    [Fact]
    public void FileMetadata_AllPropertiesSetCorrectly()
    {
        var now = DateTime.UtcNow;
        var m = new FileUploadService.Application.DTOs.FileMetadata
        {
            Id = 1L,
            ReferenceId = "FILE-META-001",
            OriginalFilename = "test.pdf",
            StoragePath = "uploads/2026/05/abc.enc",
            ContentType = "application/pdf",
            FileSize = 4096,
            UploadedBy = 1L,
            CreatedAt = now,
            Iv = "aGVsbG8=",
            IsDeleted = false
        };

        m.Id.Should().Be(1L);
        m.ReferenceId.Should().Be("FILE-META-001");
        m.OriginalFilename.Should().Be("test.pdf");
        m.StoragePath.Should().Be("uploads/2026/05/abc.enc");
        m.ContentType.Should().Be("application/pdf");
        m.FileSize.Should().Be(4096);
        m.UploadedBy.Should().Be(1L);
        m.CreatedAt.Should().Be(now);
        m.Iv.Should().Be("aGVsbG8=");
        m.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void FileMetadata_IsDeleted_DefaultsFalse()
        => new FileUploadService.Application.DTOs.FileMetadata().IsDeleted.Should().BeFalse();
}