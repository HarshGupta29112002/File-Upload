using FileUploadService.Application.Configurations;
using FileUploadService.Application.Implementation;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Services;

public class EncryptionServiceTests
{
    private readonly EncryptionService _sut;

    public EncryptionServiceTests()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var settings = Options.Create(new EncryptionSettings
        {
            AesKey = Convert.ToBase64String(key)
        });
        _sut = new EncryptionService(settings, NullLogger<EncryptionService>.Instance);
    }

    // =========================================================
    // EncryptAsync
    // =========================================================

    [Fact]
    public async Task EncryptAsync_ReturnsNonEmptyEncryptedBytes()
    {
        using var stream = PlainStream("Hello World");
        var result = await _sut.EncryptAsync(stream);
        result.EncryptedBytes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task EncryptAsync_ReturnsValidBase64Iv()
    {
        using var stream = PlainStream("Hello World");
        var result = await _sut.EncryptAsync(stream);
        var act = () => Convert.FromBase64String(result.IvBase64);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task EncryptAsync_IvIs16Bytes()
    {
        using var stream = PlainStream("Hello World");
        var result = await _sut.EncryptAsync(stream);
        Convert.FromBase64String(result.IvBase64).Length.Should().Be(16);
    }

    [Fact]
    public async Task EncryptAsync_ProducesDifferentCiphertextEachCall()
    {
        var result1 = await _sut.EncryptAsync(PlainStream("same content"));
        var result2 = await _sut.EncryptAsync(PlainStream("same content"));

        // Different IV → different ciphertext
        result1.IvBase64.Should().NotBe(result2.IvBase64);
        result1.EncryptedBytes.Should().NotEqual(result2.EncryptedBytes);
    }

    [Fact]
    public async Task EncryptAsync_EncryptedSizeIsMultipleOf16()
    {
        using var stream = PlainStream("test");
        var result = await _sut.EncryptAsync(stream);
        (result.EncryptedBytes.Length % 16).Should().Be(0);
    }

    // =========================================================
    // DecryptAsync
    // =========================================================

    [Fact]
    public async Task DecryptAsync_RoundTrip_ReturnsOriginalContent()
    {
        const string original = "Hello, encrypted world!";
        using var plain = PlainStream(original);

        var encResult = await _sut.EncryptAsync(plain);
        using var encStr = new MemoryStream(encResult.EncryptedBytes);

        var decStream = await _sut.DecryptAsync(encStr, encResult.IvBase64);
        var decText = await new StreamReader(decStream).ReadToEndAsync();

        decText.Should().Be(original);
    }

    [Fact]
    public async Task DecryptAsync_WrongIv_ThrowsInvalidOperationException()
    {
        using var plain = PlainStream("data");
        var encResult = await _sut.EncryptAsync(plain);

        var wrongIv = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        using var encStr = new MemoryStream(encResult.EncryptedBytes);

        var act = async () => await _sut.DecryptAsync(encStr, wrongIv);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DecryptAsync_InvalidBase64Iv_ThrowsInvalidOperationException()
    {
        using var encStr = new MemoryStream(new byte[32]);
        var act = async () => await _sut.DecryptAsync(encStr, "NOT_VALID_BASE64!!!");
        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*Base64*");
    }

    [Fact]
    public async Task DecryptAsync_IvWrongLength_ThrowsInvalidOperationException()
    {
        using var encStr = new MemoryStream(new byte[32]);
        var shortIv = Convert.ToBase64String(new byte[8]); // 8 bytes, not 16
        var act = async () => await _sut.DecryptAsync(encStr, shortIv);
        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*16 bytes*");
    }

    [Fact]
    public async Task DecryptAsync_LargeFile_RoundTripSucceeds()
    {
        var big = new byte[64_000];
        RandomNumberGenerator.Fill(big);
        using var plain = new MemoryStream(big);

        var encResult = await _sut.EncryptAsync(plain);
        using var enc = new MemoryStream(encResult.EncryptedBytes);
        var dec = await _sut.DecryptAsync(enc, encResult.IvBase64);

        using var ms = new MemoryStream();
        await dec.CopyToAsync(ms);
        ms.ToArray().Should().Equal(big);
    }

    // ── helpers ──────────────────────────────────────────────────
    private static MemoryStream PlainStream(string text)
        => new(Encoding.UTF8.GetBytes(text));
}