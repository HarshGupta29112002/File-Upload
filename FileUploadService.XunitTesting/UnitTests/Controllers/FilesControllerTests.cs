using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using FileUploadService.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FileUploadService.XunitTesting.UnitTests.Controllers;

public class FilesControllerTests
{
    private readonly Mock<IFileService> _fileServiceMock = new();
    private readonly Mock<ILogger<FilesController>> _loggerMock = new();
    private readonly FilesController _controller;

    public FilesControllerTests()
    {
        _controller = new FilesController(_fileServiceMock.Object, _loggerMock.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    // =========================================================
    // DownloadFile — positive
    // =========================================================

    [Fact]
    public async Task DownloadFile_ValidReferenceId_ReturnsFileStreamResult()
    {
        var (metadata, stream) = MakePdfMetadata("FILE-DL-001");
        _fileServiceMock.Setup(s => s.DownloadFileAsync("FILE-DL-001"))
                        .ReturnsAsync((metadata, stream));

        var result = await _controller.DownloadFile("FILE-DL-001");

        result.Should().BeOfType<FileStreamResult>();
    }

    [Fact]
    public async Task DownloadFile_ValidReferenceId_UsesCorrectContentType()
    {
        var (metadata, stream) = MakePdfMetadata("FILE-DL-002");
        _fileServiceMock.Setup(s => s.DownloadFileAsync("FILE-DL-002"))
                        .ReturnsAsync((metadata, stream));

        var result = await _controller.DownloadFile("FILE-DL-002") as FileStreamResult;

        result!.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task DownloadFile_ValidReferenceId_SetsContentDispositionHeader()
    {
        var metadata = new FileMetadata
        {
            OriginalFilename = "my-report.xlsx",
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSize = 512,
            CreatedAt = DateTime.UtcNow
        };
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        _fileServiceMock.Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
                        .ReturnsAsync((metadata, (Stream)stream));

        await _controller.DownloadFile("FILE-DL-003");

        _controller.Response.Headers["Content-Disposition"].ToString()
            .Should().Contain("attachment").And.Contain("my-report.xlsx");
    }

    [Fact]
    public async Task DownloadFile_NullContentType_FallsBackToOctetStream()
    {
        var metadata = new FileMetadata
        {
            OriginalFilename = "unknown.bin",
            ContentType = null,
            FileSize = 10
        };
        var stream = new MemoryStream(new byte[] { 0x00 });

        _fileServiceMock.Setup(s => s.DownloadFileAsync("FILE-BIN-001"))
                        .ReturnsAsync((metadata, (Stream)stream));

        var result = await _controller.DownloadFile("FILE-BIN-001") as FileStreamResult;

        result!.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task DownloadFile_AnyReferenceId_CallsServiceExactlyOnce()
    {
        var (metadata, stream) = MakePdfMetadata("FILE-DL-ONCE");
        _fileServiceMock.Setup(s => s.DownloadFileAsync("FILE-DL-ONCE"))
                        .ReturnsAsync((metadata, stream));

        await _controller.DownloadFile("FILE-DL-ONCE");

        _fileServiceMock.Verify(s => s.DownloadFileAsync("FILE-DL-ONCE"), Times.Once);
    }

    // =========================================================
    // DownloadFile — negative
    // =========================================================

    [Fact]
    public async Task DownloadFile_FileNotFound_Returns404()
    {
        _fileServiceMock.Setup(s => s.DownloadFileAsync("FILE-NOTFOUND"))
                        .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        var result = await _controller.DownloadFile("FILE-NOTFOUND");

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DownloadFile_FileNotFound_ResponseContainsSuccessFalse()
    {
        _fileServiceMock.Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
                        .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        var result = await _controller.DownloadFile("MISSING") as NotFoundObjectResult;
        var payload = System.Text.Json.JsonSerializer.Serialize(result!.Value);

        payload.Should().Contain("false");
    }

    [Fact]
    public async Task DownloadFile_ServiceThrows_ExceptionPropagates()
    {
        _fileServiceMock.Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
                        .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        var act = async () => await _controller.DownloadFile("FILE-ERR");

        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*DB connection lost*");
    }

    // =========================================================
    // GetFileMetadata — positive
    // =========================================================

    [Fact]
    public async Task GetFileMetadata_ValidReferenceId_ReturnsOkResult()
    {
        var metadata = MakeMetadata("FILE-META-001");
        _fileServiceMock.Setup(s => s.GetFileMetadataAsync("FILE-META-001"))
                        .ReturnsAsync(metadata);

        var result = await _controller.GetFileMetadata("FILE-META-001");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetFileMetadata_ValidReferenceId_Returns200StatusCode()
    {
        var metadata = MakeMetadata("FILE-META-002");
        _fileServiceMock.Setup(s => s.GetFileMetadataAsync("FILE-META-002"))
                        .ReturnsAsync(metadata);

        var result = await _controller.GetFileMetadata("FILE-META-002") as OkObjectResult;

        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task GetFileMetadata_ValidReferenceId_CallsGetFileMetadataAsync()
    {
        var metadata = MakeMetadata("FILE-META-003");
        _fileServiceMock.Setup(s => s.GetFileMetadataAsync("FILE-META-003"))
                        .ReturnsAsync(metadata);

        await _controller.GetFileMetadata("FILE-META-003");

        _fileServiceMock.Verify(s => s.GetFileMetadataAsync("FILE-META-003"), Times.Once);
    }

    // =========================================================
    // GetFileMetadata — negative
    // =========================================================

    [Fact]
    public async Task GetFileMetadata_FileNotFound_Returns404()
    {
        _fileServiceMock.Setup(s => s.GetFileMetadataAsync(It.IsAny<string>()))
                        .ReturnsAsync((FileMetadata?)null);

        var result = await _controller.GetFileMetadata("NOPE");

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    // =========================================================
    // DeleteFile — positive
    // =========================================================

    [Fact]
    public async Task DeleteFile_ExistingFile_ReturnsOkResult()
    {
        _fileServiceMock.Setup(s => s.DeleteFileAsync("FILE-DEL-001"))
                        .ReturnsAsync(true);

        var result = await _controller.DeleteFile("FILE-DEL-001");

        result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DeleteFile_ExistingFile_ResponseContainsSuccessTrue()
    {
        _fileServiceMock.Setup(s => s.DeleteFileAsync("FILE-DEL-002"))
                        .ReturnsAsync(true);

        var result = await _controller.DeleteFile("FILE-DEL-002") as OkObjectResult;
        var payload = System.Text.Json.JsonSerializer.Serialize(result!.Value);

        payload.Should().Contain("true");
    }

    // =========================================================
    // DeleteFile — negative
    // =========================================================

    [Fact]
    public async Task DeleteFile_NonExistentFile_Returns404()
    {
        _fileServiceMock.Setup(s => s.DeleteFileAsync(It.IsAny<string>()))
                        .ReturnsAsync(false);

        var result = await _controller.DeleteFile("FILE-GONE-999");

        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeleteFile_NonExistentFile_ResponseContainsSuccessFalse()
    {
        _fileServiceMock.Setup(s => s.DeleteFileAsync(It.IsAny<string>()))
                        .ReturnsAsync(false);

        var result = await _controller.DeleteFile("FILE-GONE-888") as NotFoundObjectResult;
        var payload = System.Text.Json.JsonSerializer.Serialize(result!.Value);

        payload.Should().Contain("false");
    }

    // =========================================================
    // UploadFile
    // =========================================================

    [Fact]
    public async Task UploadFile_ValidRequest_Returns201()
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("photo.jpg");
        fileMock.Setup(f => f.Length).Returns(1024);

        _fileServiceMock
            .Setup(s => s.UploadFileAsync(It.IsAny<IFormFile>(), It.IsAny<string?>()))
            .ReturnsAsync(new FileUploadResponse
            {
                ReferenceId = "FILE-20260516-UPLOAD",
                OriginalFilename = "photo.jpg",
                ContentType = "image/jpeg",
                FileSizeBytes = 1024,
                CreatedAt = DateTime.UtcNow
            });

        var request = new FileUploadRequest { File = fileMock.Object };
        var result = await _controller.UploadFile(request) as ObjectResult;

        result!.StatusCode.Should().Be(201);
    }

    // =========================================================
    // Helpers
    // =========================================================

    private static (FileMetadata, Stream) MakePdfMetadata(string referenceId)
    {
        var metadata = new FileMetadata
        {
            ReferenceId = referenceId,
            OriginalFilename = "document.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
        Stream stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        return (metadata, stream);
    }

    private static FileMetadata MakeMetadata(string referenceId) => new()
    {
        Id = 1L,
        ReferenceId = referenceId,
        OriginalFilename = "spreadsheet.xlsx",
        ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        FileSize = 8192,
        CreatedAt = new DateTime(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc),
        IsDeleted = false
    };
}