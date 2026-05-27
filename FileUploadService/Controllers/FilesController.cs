using Microsoft.AspNetCore.Mvc;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;

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

    [HttpPost("upload")]
    [RequestSizeLimit(52_428_800)] // 50MB
    [Consumes("multipart/form-data")] // Ensure the endpoint only accepts multipart/form-data requests
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadFile([FromForm] FileUploadRequest request)
    {
        _logger.LogInformation(
            "Upload request received. Filename: {Filename}, Size: {Size} bytes",
            request.File?.FileName,
            request.File?.Length
        );

        var response = await _fileService.UploadFileAsync(request.File, request.UploadedBy);
        return StatusCode(StatusCodes.Status201Created, new
        {
            success = true,
            message = "File uploaded successfully.",
            data = response
        });
    }

    //  GET /api/files/{referenceId}
    [HttpGet("{referenceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile([FromRoute] string referenceId)
    {
        _logger.LogInformation("Download request received. ReferenceId: {ReferenceId}", referenceId);

        var result = await _fileService.DownloadFileAsync(referenceId);

        if (result is null)
        {
            return NotFound(new
            {
                success = false,
                message = $"File with reference ID '{referenceId}' was not found."
            });
        }

        var (metadata, fileStream) = result.Value;

        Response.Headers.Append(
            "Content-Disposition",
            $"attachment; filename=\"{metadata.OriginalFilename}\""
        );

        return File(fileStream, metadata.ContentType ?? "application/octet-stream");
    }

    //  GET /api/files/{referenceId}/metadata
    [HttpGet("{referenceId}/metadata")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)] //------------------
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFileMetadata([FromRoute] string referenceId)
    {
        // BUG FIX: Previously called DownloadFileAsync which read the entire file
        // from disk and decrypted it just to return metadata fields.
        // Now uses GetFileMetadataAsync which only queries the database.
        var metadata = await _fileService.GetFileMetadataAsync(referenceId);

        if (metadata is null)
        {
            return NotFound(new
            {
                success = false,
                message = $"File with reference ID '{referenceId}' was not found."
            });
        }

        return Ok(new
        {
            success = true,
            data = new
            {
                metadata.ReferenceId,
                metadata.OriginalFilename,
                metadata.ContentType,
                metadata.FileSize,
                metadata.CreatedAt
            }
        });
    }
    //  DELETE /api/files/{referenceId}
    [HttpDelete("{referenceId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile([FromRoute] string referenceId)
    {
        _logger.LogInformation("Delete request received. ReferenceId: {ReferenceId}", referenceId);

        var deleted = await _fileService.DeleteFileAsync(referenceId);

        if (!deleted)
        {
            return NotFound(new
            {
                success = false,
                message = $"File with reference ID '{referenceId}' was not found or is already deleted."
            });
        }

        return Ok(new
        {
            success = true,
            message = $"File '{referenceId}' has been deleted."
        });
    }
}