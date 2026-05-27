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
    private readonly Mock<IFileService>              _fileServiceMock = new();
    private readonly Mock<ILogger<FilesController>> _loggerMock      = new();
    private readonly FilesController                _controller;

    public FilesControllerTests()
    {
        _controller = new FilesController(_fileServiceMock.Object, _loggerMock.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    // POSITIVE TEST CASES — DownloadFile

    [Fact]
    public async Task DownloadFile_ValidReferenceId_ReturnsFileResult()
    {
        // Arrange
        var metadata = new FileMetadata
        {
            ReferenceId      = "FILE-20260516-Harsh1",
            OriginalFilename = "document.pdf",
            ContentType      = "application/pdf",
            FileSize         = 2048,
            CreatedAt        = DateTime.UtcNow,
            IsDeleted        = false
        };
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // PDF magic bytes

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-20260516-Harsh1"))
            .ReturnsAsync((metadata, (Stream)stream));

        // Act
        var result = await _controller.DownloadFile("FILE-20260516-Harsh1");

        // Assert
        result.Should().BeOfType<FileStreamResult>();
    }


    [Fact]
    public async Task DownloadFile_ValidReferenceId_SetsContentDispositionHeader()
    {
        // Arrange
        var metadata = new FileMetadata
        {
            OriginalFilename = "my-report.xlsx",
            ContentType      = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSize         = 512,
            CreatedAt        = DateTime.UtcNow
        };
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((metadata, (Stream)stream));

        // Act
        await _controller.DownloadFile("FILE-XYZ");

        // Assert
        _controller.Response.Headers["Content-Disposition"].ToString()
            .Should().Contain("attachment");
        _controller.Response.Headers["Content-Disposition"].ToString()
            .Should().Contain("my-report.xlsx");
    }


    [Fact]
    public async Task DownloadFile_ValidReferenceId_UsesCorrectContentType()
    {
        // Arrange
        var metadata = new FileMetadata
        {
            OriginalFilename = "photo.jpg",
            ContentType      = "image/jpeg",
            FileSize         = 100,
            CreatedAt        = DateTime.UtcNow
        };
        var stream = new MemoryStream(new byte[] { 0xFF, 0xD8 }); // JPEG magic

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-IMG-001"))
            .ReturnsAsync((metadata, (Stream)stream));

        // Act
        var result = await _controller.DownloadFile("FILE-IMG-001") as FileStreamResult;

        // Assert
        result!.ContentType.Should().Be("image/jpeg");
    }


    [Fact]
    public async Task GetFileMetadata_ValidReferenceId_ReturnsOkWithData()
    {
        // Arrange
        var metadata = new FileMetadata
        {
            ReferenceId      = "FILE-META-Harsh",
            OriginalFilename = "spreadsheet.xlsx",
            ContentType      = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileSize         = 8192,
            CreatedAt        = new DateTime(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc)
        };
        var stream = new MemoryStream(new byte[] { 1 });

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-META-Harsh"))
            .ReturnsAsync((metadata, (Stream)stream));

        // Act
        var result = await _controller.GetFileMetadata("FILE-META-Harsh") as OkObjectResult;

        // Assert
        result.Should().NotBeNull();
        result!.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task DownloadFile_AnyReferenceId_CallsServiceOnce()
    {
        // Arrange
        var metadata = new FileMetadata { OriginalFilename = "f.pdf", ContentType = "application/pdf" };
        var stream   = new MemoryStream(new byte[] { 1 });

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-ONCE"))
            .ReturnsAsync((metadata, (Stream)stream));

        // Act
        await _controller.DownloadFile("FILE-ONCE");

        // Assert
        _fileServiceMock.Verify(s => s.DownloadFileAsync("FILE-ONCE"), Times.Once);
    }

    // NEGATIVE TEST CASES — DownloadFile

    [Fact]
    public async Task DownloadFile_FileNotFound_Returns404()
    {
        // Arrange
        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-NOTFOUND"))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        // Act
        var result = await _controller.DownloadFile("FILE-NOTFOUND");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DownloadFile_FileNotFound_ResponseContainsSuccessFalse()
    {
        // Arrange
        _fileServiceMock
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        // Act
        var result  = await _controller.DownloadFile("MISSING") as NotFoundObjectResult;
        var payload = System.Text.Json.JsonSerializer.Serialize(result!.Value);

        // Assert
        payload.Should().Contain("false");
    }

    [Fact]
    public async Task GetFileMetadata_FileNotFound_Returns404()
    {
        // Arrange
        _fileServiceMock
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ReturnsAsync((ValueTuple<FileMetadata, Stream>?)null);

        // Act
        var result = await _controller.GetFileMetadata("NOPE");

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DownloadFile_ServiceThrows_ExceptionPropagates()
    {
        // Arrange
        _fileServiceMock
            .Setup(s => s.DownloadFileAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("DB connection lost"));

        // Act
        var act = async () => await _controller.DownloadFile("FILE-ERR");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*DB connection lost*");
    }

    [Fact]
    public async Task DownloadFile_NullContentType_FallsBackToOctetStream()
    {
        // Arrange
        var metadata = new FileMetadata
        {
            OriginalFilename = "unknown.bin",
            ContentType      = null,   // explicitly null
            FileSize         = 10
        };
        var stream = new MemoryStream(new byte[] { 0x00 });

        _fileServiceMock
            .Setup(s => s.DownloadFileAsync("FILE-BIN"))
            .ReturnsAsync((metadata, (Stream)stream));

        // Act
        var result = await _controller.DownloadFile("FILE-BIN") as FileStreamResult;

        // Assert
        result!.ContentType.Should().Be("application/octet-stream");
    }
}
