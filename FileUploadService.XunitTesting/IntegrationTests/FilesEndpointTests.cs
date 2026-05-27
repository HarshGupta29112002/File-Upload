using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
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
    // POSITIVE TEST CASES — DOWNLOAD
    // =========================================================

    // ── Known referenceId → 200 with file stream ──────────────────

    [Fact]
    public async Task GetFile_KnownReferenceId_Returns200()
    {
        // Arrange
        _factory.SetupHappyDownload("FILE-20260516-harsh");

        // Act
        var response = await _client.GetAsync("/api/files/FILE-20260516-harsh");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Download response has Content-Disposition header ──────────

    [Fact]
    public async Task GetFile_KnownReferenceId_HasContentDispositionHeader()
    {
        // Arrange
        _factory.SetupHappyDownload("FILE-20260516-harsh");

        // Act
        var response = await _client.GetAsync("/api/files/FILE-20260516-harsh");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    // ── Metadata endpoint returns structured JSON ──────────────────

    [Fact]
    public async Task GetMetadata_KnownReferenceId_ReturnsJsonWithFields()
    {
        // Arrange
        _factory.SetupHappyDownload("FILE-META-001");

        // Act
        var response = await _client.GetAsync("/api/files/FILE-META-001/metadata");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.TryGetProperty("data", out _).Should().BeTrue();
    }

    // ── Metadata returns correct referenceId ───────────────────────

    [Fact]
    public async Task GetMetadata_KnownReferenceId_ContainsReferenceId()
    {
        // Arrange
        _factory.SetupHappyDownload("FILE-META-002");

        // Act
        var response = await _client.GetAsync("/api/files/FILE-META-002/metadata");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        var data = json.GetProperty("data");
        data.GetProperty("referenceId").GetString().Should().Be("FILE-META-002");
    }

    // =========================================================
    // NEGATIVE TEST CASES — DOWNLOAD
    // =========================================================

    // ── Unknown referenceId → 404 ─────────────────────────────────

    [Fact]
    public async Task GetFile_UnknownReferenceId_Returns404()
    {
        // Arrange
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DownloadFileAsync("FILE-NOTFOUND-000"))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        // Act
        var response = await _client.GetAsync("/api/files/FILE-NOTFOUND-000");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── 404 response body contains success=false ──────────────────

    [Fact]
    public async Task GetFile_UnknownReferenceId_ResponseBodyHasSuccessFalse()
    {
        // Arrange
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        // Act
        var response = await _client.GetAsync("/api/files/NO-EXIST-123");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Assert
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    // ── Metadata endpoint for unknown ID returns 404 ──────────────

    [Fact]
    public async Task GetMetadata_UnknownReferenceId_Returns404()
    {
        // Arrange
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        // Act
        var response = await _client.GetAsync("/api/files/NOPE-999/metadata");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── FileService throws unexpected exception → 500 ─────────────

    [Fact]
    public async Task GetFile_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("database blew up"));

        // Act
        var response = await _client.GetAsync("/api/files/FILE-ERR-001");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    // ── Wrong HTTP method → 405 ───────────────────────────────────

    [Fact]
    public async Task Delete_FileEndpoint_Returns405()
    {
        // Act
        var response = await _client.DeleteAsync("/api/files/FILE-20260516-harsh");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    // ── Unknown route → 404 ───────────────────────────────────────

    [Fact]
    public async Task Get_UnknownRoute_Returns404()
    {
        // Act
        var response = await _client.GetAsync("/api/unknown/route");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =========================================================
    // UPLOAD ENDPOINT — POST /api/files/upload
    // =========================================================

    // ── Valid file → 201 Created ───────────────────────────────

    [Fact]
    public async Task UploadFile_ValidFile_Returns201()
    {
        // Arrange
        _factory.SetupHappyUpload("FILE-20260519-UP001");
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[600];
        fileBytes[0] = 0xFF; fileBytes[1] = 0xD8; fileBytes[2] = 0xFF; fileBytes[3] = 0xE0;
        content.Add(new ByteArrayContent(fileBytes), "file", "photo.jpg");

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    }

    // ── Valid file → response body has success=true and referenceId ───

    [Fact]
    public async Task UploadFile_ValidFile_ResponseContainsReferenceId()
    {
        // Arrange
        _factory.SetupHappyUpload("FILE-20260519-UP002");
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[600];
        fileBytes[0] = 0xFF; fileBytes[1] = 0xD8; fileBytes[2] = 0xFF; fileBytes[3] = 0xE0;
        content.Add(new ByteArrayContent(fileBytes), "file", "photo.jpg");

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);
        var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        // Assert
        json.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetProperty("referenceId").GetString().Should().Be("FILE-20260519-UP002");
    }

    // ── Valid file with uploadedBy header → 201 ───────────────

    [Fact]
    public async Task UploadFile_WithUploadedBy_Returns201()
    {
        // Arrange
        _factory.SetupHappyUpload("FILE-20260519-UP003");
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[600];
        fileBytes[0] = 0xFF; fileBytes[1] = 0xD8; fileBytes[2] = 0xFF; fileBytes[3] = 0xE0;
        content.Add(new ByteArrayContent(fileBytes), "file", "photo.jpg");
        content.Add(new StringContent(Guid.NewGuid().ToString()), "uploadedBy");

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
    }

    // ── Service throws FileValidationException → 400 ──────────

    [Fact]
    public async Task UploadFile_ValidationFails_Returns400()
    {
        // Arrange
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

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
    }

    // ── Service throws VirusDetectedException → 422 ───────────

    [Fact]
    public async Task UploadFile_VirusDetected_Returns422()
    {
        // Arrange
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new VirusDetectedException("Win.Test.EICAR_HDB-1"));

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "infected.pdf");

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);

        // Assert
        ((int)response.StatusCode).Should().Be(422);
    }

    // ── Service throws VirusScanException → 503 ───────────────

    [Fact]
    public async Task UploadFile_ScannerUnavailable_Returns503()
    {
        // Arrange
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new VirusScanException("ClamAV not responding"));

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "test.pdf");

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);

        // Assert
        ((int)response.StatusCode).Should().Be(503);
    }

    // ── Service throws generic exception → 500 ────────────────

    [Fact]
    public async Task UploadFile_ServiceThrows_Returns500()
    {
        // Arrange
        _factory.ResetMocks();
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ThrowsAsync(new Exception("unexpected error"));

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x00 }), "file", "test.pdf");

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.InternalServerError);
    }

    // ── IFileService.UploadFileAsync called exactly once ──────

    [Fact]
    public async Task UploadFile_ValidFile_CallsServiceOnce()
    {
        // Arrange
        _factory.SetupHappyUpload("FILE-20260519-UP004");
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[600];
        fileBytes[0] = 0xFF; fileBytes[1] = 0xD8; fileBytes[2] = 0xFF; fileBytes[3] = 0xE0;
        content.Add(new ByteArrayContent(fileBytes), "file", "photo.jpg");

        // Act
        await _client.PostAsync("/api/files/upload", content);

        // Assert
        _factory.FileService.Verify(
            s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()),
            Times.Once);
    }

    // ── Response Content-Type is application/json ─────────────

    [Fact]
    public async Task UploadFile_ValidFile_ResponseIsJson()
    {
        // Arrange
        _factory.SetupHappyUpload("FILE-20260519-UP005");
        using var content = new MultipartFormDataContent();
        var fileBytes = new byte[600];
        fileBytes[0] = 0xFF; fileBytes[1] = 0xD8; fileBytes[2] = 0xFF; fileBytes[3] = 0xE0;
        content.Add(new ByteArrayContent(fileBytes), "file", "photo.jpg");

        // Act
        var response = await _client.PostAsync("/api/files/upload", content);

        // Assert
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");
    }

}