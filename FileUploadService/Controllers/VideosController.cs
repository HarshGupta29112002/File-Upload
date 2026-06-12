using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FileUploadService.Controllers;

[ApiController]
[Route("api/videos")]
public class VideosController : ControllerBase
{
    private readonly IVideoService _videoService;
    private readonly ILogger<VideosController> _logger;

    public VideosController(IVideoService videoService, ILogger<VideosController> logger)
    {
        _videoService = videoService;
        _logger = logger;
    }

    // ── POST /api/videos/upload ───────────────────────────────
    /// <summary>
    /// Uploads an MP4 video. Performs MIME validation, virus scan,
    /// streams to disk, and extracts metadata via FFprobe.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(524_288_000)]            // 500 MB — matches VideoStorageSettings default
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<VideoUploadResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadVideo(
        [FromForm] VideoUploadRequest request,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Video upload request. Filename: {Name}, Size: {Size} bytes",
            request.File?.FileName, request.File?.Length
        );

        if (request.File is null || request.File.Length == 0)
            return BadRequest(ApiResponse.Fail("No video file provided."));

        var response = await _videoService.UploadVideoAsync(request.File, request.UploadedBy);

        return StatusCode(
            StatusCodes.Status201Created,
            ApiResponse<VideoUploadResponse>.Ok(response, "Video uploaded successfully.")
        );
    }

    // ── GET /api/videos/{referenceId} ────────────────────────
    /// <summary>
    /// Streams the video file back to the client.
    /// enableRangeProcessing: true allows browsers/players to seek without downloading the full file.
    /// </summary>
    [HttpGet("{referenceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status206PartialContent)]   // range requests
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadVideo(
        [FromRoute] string referenceId,
        CancellationToken ct)
    {
        _logger.LogInformation("Video download request. ReferenceId: {Ref}", referenceId);

        var result = await _videoService.DownloadVideoAsync(referenceId);

        if (result is null)
            return NotFound(ApiResponse.Fail($"Video '{referenceId}' was not found."));

        var (metadata, stream) = result.Value;

        // enableRangeProcessing: true  →  ASP.NET Core handles Range headers automatically.
        // This lets video players (browser <video> tag, VLC, etc.) seek to any timestamp
        // without downloading the whole file first.
        return File(
            stream,
            metadata.ContentType ?? "video/mp4",
            metadata.OriginalFilename,
            enableRangeProcessing: true
        );
    }
}