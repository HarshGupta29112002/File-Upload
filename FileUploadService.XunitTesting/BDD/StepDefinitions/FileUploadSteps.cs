using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using FileUploadService.XunitTesting.IntegrationTests.Helpers;
using FluentAssertions;
using Moq;
using Reqnroll;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace FileUploadService.XunitTesting.BDD.StepDefinitions;

/// <summary>
/// Step definitions for FileUpload.feature.
/// Uses the shared TestWebApplicationFactory (injected by Reqnroll via constructor binding)
/// exactly the same way IpLookupSteps uses it in IplookupService.XunitTesting.
/// </summary>
[Binding]
public class FileUploadSteps
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient                _client;

    private HttpResponseMessage _response  = default!;
    private JsonElement         _body;
    private string              _referenceId = string.Empty;

    public FileUploadSteps(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // ─────────────────────────────────────────────
    // GIVEN
    // ─────────────────────────────────────────────

    [Given(@"the file upload service is running")]
    public void GivenServiceIsRunning()
    {
        _factory.ResetMocks();
    }

    [Given(@"a valid PDF file named ""(.*)"" of size (.*) bytes")]
    public void GivenAValidPdfFile(string filename, int size)
    {
        // Setup upload mock — the actual multipart POST is hard to fake in BDD
        // so this step configures what the service returns when called
        _factory.FileService
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ReferenceId      = "FILE-20260516-BDD01",
                OriginalFilename = filename,
                ContentType      = "application/pdf",
                FileSizeBytes    = size,
                CreatedAt        = DateTime.UtcNow
            });
    }

    [Given(@"a file has been uploaded with reference ID ""(.*)""")]
    public void GivenFileExistsWithReferenceId(string referenceId)
    {
        _referenceId = referenceId;

        var metadata = new FileMetadata
        {
            Id               = 1L,
            ReferenceId      = referenceId,
            OriginalFilename = "bdd-test-file.pdf",
            ContentType      = "application/pdf",
            FileSize         = 2048,
            CreatedAt        = DateTime.UtcNow,
            IsDeleted        = false,
            Iv               = Convert.ToBase64String(new byte[16])
        };
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });

        _factory.FileService
            .Setup(s => s.DownloadFileAsync(referenceId))
            .ReturnsAsync((metadata, (Stream)stream));
    }

    [Given(@"no file exists with reference ID ""(.*)""")]
    public void GivenNoFileExistsWithReferenceId(string referenceId)
    {
        _referenceId = referenceId;

        _factory.FileService
            .Setup(s => s.DownloadFileAsync(referenceId))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);
    }

    [Given(@"the file service is unavailable")]
    public void GivenFileServiceUnavailable()
    {
        _factory.FileService
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ThrowsAsync(new Exception("PostgreSQL connection refused"));
    }

    // ─────────────────────────────────────────────
    // WHEN
    // ─────────────────────────────────────────────

    [When(@"I upload the file")]
    public async Task WhenIUploadTheFile()
    {
        // Simulate a multipart upload with a tiny content (BDD smoke test)
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 }),
            "file", "report.pdf");

        _response = await _client.PostAsync("/api/files/upload", content);

        if (_response.Content.Headers.ContentLength != 0)
            _body = await _response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [When(@"I download the file with reference ID ""(.*)""")]
    public async Task WhenIDownloadFile(string referenceId)
    {
        _referenceId = referenceId;
        _response    = await _client.GetAsync($"/api/files/{referenceId}");
    }

    [When(@"I get metadata for reference ID ""(.*)""")]
    public async Task WhenIGetMetadata(string referenceId)
    {
        _referenceId = referenceId;
        _response    = await _client.GetAsync($"/api/files/{referenceId}/metadata");

        if (_response.Content.Headers.ContentLength != 0)
            _body = await _response.Content.ReadFromJsonAsync<JsonElement>();
    }

    // ─────────────────────────────────────────────
    // THEN
    // ─────────────────────────────────────────────

    [Then(@"the response should be 201 Created")]
    public void ThenResponse201()
    {
        _response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Then(@"the response should be 200 OK")]
    public void ThenResponse200()
    {
        _response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Then(@"the response should contain a reference_id")]
    public void ThenContainsReferenceId()
    {
        _body.TryGetProperty("data", out var data).Should().BeTrue();
        data.TryGetProperty("referenceId", out _).Should().BeTrue();
    }

    [Then(@"the response should have a Content-Disposition attachment header")]
    public void ThenHasContentDisposition()
    {
        _response.Content.Headers.ContentDisposition.Should().NotBeNull();
        _response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    [Then(@"the metadata should contain a referenceId field")]
    public void ThenMetadataContainsReferenceId()
    {
        _body.GetProperty("data").TryGetProperty("referenceId", out _).Should().BeTrue();
    }

    [Then(@"the response status should be (\d+)")]
    public void ThenResponseStatusShouldBe(int statusCode)
    {
        ((int)_response.StatusCode).Should().Be(statusCode);
    }
}
