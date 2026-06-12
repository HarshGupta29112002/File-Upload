using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FileUploadService.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;
    private readonly ILogger<FilesController> _logger;

    public FilesController(IFileService fileService, ILogger<FilesController> logger)
    {
        _fileService = fileService;
        _logger = logger;
    }

    // POST /api/files/upload
    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<FileUploadResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadFile([FromForm] FileUploadRequest request)
    {
        _logger.LogInformation(
            "Upload request. Filename: {Filename}, Size: {Size} bytes",
            request.File?.FileName, request.File?.Length
        );

        var response = await _fileService.UploadFileAsync(request.File!, request.UploadedBy);

        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<FileUploadResponse>.Ok(response, "File uploaded successfully.")
        );
    }

    // GET /api/files/{referenceId}
    [HttpGet("{referenceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile([FromRoute] string referenceId)
    {
        _logger.LogInformation("Download request. ReferenceId: {Ref}", referenceId);

        var result = await _fileService.DownloadFileAsync(referenceId);

        if (result is null)
            return NotFound(ApiResponse.Fail($"File '{referenceId}' was not found."));

        var (metadata, fileStream) = result.Value;

        Response.Headers.Append(
            "Content-Disposition",
            $"attachment; filename=\"{metadata.OriginalFilename}\""
        );

        return File(fileStream, metadata.ContentType ?? "application/octet-stream");
    }

    // GET /api/files/{referenceId}/metadata
    [HttpGet("{referenceId}/metadata")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileMetadata([FromRoute] string referenceId)
    {
        var metadata = await _fileService.GetFileMetadataAsync(referenceId);

        if (metadata is null)
            return NotFound(ApiResponse.Fail($"File '{referenceId}' was not found."));

        return Ok(ApiResponse<object>.Ok(new
        {
            metadata.ReferenceId,
            metadata.OriginalFilename,
            metadata.ContentType,
            metadata.FileSize,
            metadata.CreatedAt
        }));
    }

    // DELETE /api/files/{referenceId}
    [HttpDelete("{referenceId}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile([FromRoute] string referenceId)
    {
        _logger.LogInformation("Delete request. ReferenceId: {Ref}", referenceId);

        var deleted = await _fileService.DeleteFileAsync(referenceId);

        if (!deleted)
            return NotFound(ApiResponse.Fail(
                $"File '{referenceId}' was not found or is already deleted."
            ));

        return Ok(ApiResponse.Ok($"File '{referenceId}' has been deleted."));
    }
}