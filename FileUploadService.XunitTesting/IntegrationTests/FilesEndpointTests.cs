using FileUploadService.Application.DTOs;
using FileUploadService.XunitTesting.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FileUploadService.XunitTesting.IntegrationTests;

public class FilesEndpointTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FilesEndpointTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // =========================================================
    // DOWNLOAD — GET /api/files/{referenceId}
    // =========================================================

    [Fact]
    public async Task GetFile_KnownReferenceId_Returns200()
    {
        _factory.SetupHappyDownload("FILE-20260516-AA0001");
        var response = await _client.GetAsync("/api/files/FILE-20260516-AA0001");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetFile_KnownReferenceId_HasContentDispositionHeader()
    {
        _factory.SetupHappyDownload("FILE-20260516-AA0002");
        var response = await _client.GetAsync("/api/files/FILE-20260516-AA0002");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    [Fact]
    public async Task GetFile_UnknownReferenceId_Returns404()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DownloadFileAsync("FILE-NOTFOUND-000"))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        var response = await _client.GetAsync("/api/files/FILE-NOTFOUND-000");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetFile_UnknownReferenceId_ResponseBodyHasSuccessFalse()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        var response = await _client.GetAsync("/api/files/NO-EXIST-123");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task GetFile_ServiceThrowsException_Returns500()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("database error"));

        var response = await _client.GetAsync("/api/files/FILE-ERR-001");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    // =========================================================
    // METADATA — GET /api/files/{referenceId}/metadata
    // =========================================================

    [Fact]
    public async Task GetMetadata_KnownReferenceId_Returns200()
    {
        _factory.SetupHappyDownload("FILE-META-001");
        var response = await _client.GetAsync("/api/files/FILE-META-001/metadata");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMetadata_KnownReferenceId_ReturnsJsonWithSuccessTrue()
    {
        _factory.SetupHappyDownload("FILE-META-002");
        var response = await _client.GetAsync("/api/files/FILE-META-002/metadata");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.TryGetProperty("data", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetMetadata_KnownReferenceId_ContainsReferenceId()
    {
        _factory.SetupHappyDownload("FILE-META-003");
        var response = await _client.GetAsync("/api/files/FILE-META-003/metadata");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("data").GetProperty("referenceId").GetString()
            .Should().Be("FILE-META-003");
    }

    [Fact]
    public async Task GetMetadata_UnknownReferenceId_Returns404()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.GetFileMetadataAsync(It.IsAny<string>()))
            .ReturnsAsync((FileMetadata?)null);

        var response = await _client.GetAsync("/api/files/NOPE-999/metadata");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================
    // DELETE — DELETE /api/files/{referenceId}
    // =========================================================

    [Fact]
    public async Task DeleteFile_ExistingFile_Returns200()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DeleteFileAsync("FILE-DEL-001"))
            .ReturnsAsync(true);

        var response = await _client.DeleteAsync("/api/files/FILE-DEL-001");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DeleteFile_ExistingFile_ResponseHasSuccessTrue()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DeleteFileAsync("FILE-DEL-002"))
            .ReturnsAsync(true);

        var response = await _client.DeleteAsync("/api/files/FILE-DEL-002");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task DeleteFile_NonExistentFile_Returns404()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DeleteFileAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var response = await _client.DeleteAsync("/api/files/FILE-GONE-999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteFile_NonExistentFile_ResponseHasSuccessFalse()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DeleteFileAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        var response = await _client.DeleteAsync("/api/files/FILE-GONE-888");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    // =========================================================
    // UPLOAD — POST /api/files/upload
    // =========================================================

    [Fact]
    public async Task UploadFile_ValidJpeg_Returns201()
    {
        _factory.SetupHappyUpload("FILE-20260516-UP001");
        using var content = BuildJpegContent();
        var response = await _client.PostAsync("/api/files/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UploadFile_ValidFile_ResponseContainsReferenceId()
    {
        _factory.SetupHappyUpload("FILE-20260516-UP002");
        using var content = BuildJpegContent();
        var response = await _client.PostAsync("/api/files/upload", content);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("data").GetProperty("referenceId").GetString()
            .Should().Be("FILE-20260516-UP002");
    }

    [Fact]
    public async Task UploadFile_ValidFile_ResponseIsJson()
    {
        _factory.SetupHappyUpload("FILE-20260516-UP003");
        using var content = BuildJpegContent();
        var response = await _client.PostAsync("/api/files/upload", content);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task UploadFile_ValidFile_CallsServiceOnce()
    {
        _factory.SetupHappyUpload("FILE-20260516-UP004");
        using var content = BuildJpegContent();
        await _client.PostAsync("/api/files/upload", content);
        _factory.FileService.Verify(
            s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadFile_ValidationFails_Returns400()
    {
        _factory.ResetMocks();
        var validationResult = new FileValidationResult
        {
            IsValid = false,
            FailureReason = "File type not allowed",
            Details = new ValidationDetails { ClaimedExtension = ".exe" }
        };
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new FileValidationException(validationResult));

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x4D, 0x5A }), "file", "malware.exe");

        var response = await _client.PostAsync("/api/files/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadFile_VirusDetected_Returns422()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new VirusDetectedException("Win.Test.EICAR_HDB-1"));

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "infected.pdf");

        var response = await _client.PostAsync("/api/files/upload", content);
        ((int)response.StatusCode).Should().Be(422);
    }

    [Fact]
    public async Task UploadFile_ScannerUnavailable_Returns503()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new VirusScanException("ClamAV not responding"));

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "test.pdf");

        var response = await _client.PostAsync("/api/files/upload", content);
        ((int)response.StatusCode).Should().Be(503);
    }

    [Fact]
    public async Task UploadFile_ServiceThrows_Returns500()
    {
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("unexpected error"));

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "test.pdf");

        var response = await _client.PostAsync("/api/files/upload", content);
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Get_UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/api/unknown/route");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── helpers ──────────────────────────────────────────────────
    private static MultipartFormDataContent BuildJpegContent()
    {
        var content = new MultipartFormDataContent();
        var fileBytes = new byte[600];
        fileBytes[0] = 0xFF; fileBytes[1] = 0xD8;
        fileBytes[2] = 0xFF; fileBytes[3] = 0xE0;
        content.Add(new ByteArrayContent(fileBytes), "file", "photo.jpg");
        return content;
    }
}