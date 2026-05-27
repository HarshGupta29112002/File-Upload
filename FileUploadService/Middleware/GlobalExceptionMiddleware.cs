using System.Net;
using System.Text.Json;
using FileUploadService.Application.DTOs;

namespace FileUploadService.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (FileValidationException ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogWarning(
                "[{CorrelationId}] File validation failed: {Reason}",
                correlationId, ex.Message
            );

            var errorResponse = new
            {
                success = false,
                correlationId = correlationId,
                message = ex.ValidationResult.FailureReason,
                details = new
                {
                    extension = ex.ValidationResult.Details.ClaimedExtension,
                    mimeType = ex.ValidationResult.Details.ClaimedMimeType,
                    detectedType = ex.ValidationResult.Details.DetectedFileType,
                    fileSizeBytes = ex.ValidationResult.Details.FileSizeBytes
                }
            };

            await WriteErrorResponseAsync(context, HttpStatusCode.BadRequest, errorResponse);
        }

        catch (VirusDetectedException ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogWarning(
                "[{CorrelationId}] Infected file blocked. Threat: {Threat}",
                correlationId, ex.ThreatName
            );

            var errorResponse = new
            {
                success = false,
                correlationId = correlationId,
                message = "File rejected — virus or malware detected.",
                details = new { virusName = ex.ThreatName }
            };

            await WriteErrorResponseAsync(context, HttpStatusCode.UnprocessableEntity, errorResponse);
        }
        catch (VirusScanException ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogError(
                "[{CorrelationId}] Virus scanner unavailable: {Message}",
                correlationId, ex.Message
            );

            var errorResponse = new
            {
                success = false,
                correlationId = correlationId,
                message = ex.Message
            };

            await WriteErrorResponseAsync(context, HttpStatusCode.ServiceUnavailable, errorResponse);
        }

        catch (Exception ex)
        {
            var correlationId = GetCorrelationId(context);
            _logger.LogError(ex,
                "[{CorrelationId}] Unexpected error occurred. {ExceptionType}: {ExceptionMessage}",
                correlationId,
                // below two line are for temporary testing
                ex.GetType().Name,   // ← tells you exactly which exception type
                ex.Message           // ← the actual message
            );

            var errorResponse = new
            {
                success = false,
                correlationId = correlationId,
                // this message is temporary 
                message = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
            ? $"[DEV] {ex.GetType().Name}: {ex.Message}"
            : "An unexpected error occurred. Please try again later."
                //message = "An unexpected error occurred. Please try again later."
            };

            await WriteErrorResponseAsync(context, HttpStatusCode.InternalServerError, errorResponse);
        }
    }

    private static string GetCorrelationId(HttpContext context) =>
        context.Items[CorrelationIdMiddleware.HeaderName]?.ToString() ?? "N/A";

    private static async Task WriteErrorResponseAsync(HttpContext context, HttpStatusCode statusCode, object responseBody)
    {
        if (context.Response.HasStarted || !context.Response.Body.CanWrite)
            return;

        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        var json = JsonSerializer.Serialize(responseBody, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
