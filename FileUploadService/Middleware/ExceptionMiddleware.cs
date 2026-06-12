using FileUploadService.Application.DTOs;
using System.Net;
using System.Text.Json;

namespace FileUploadService.Middleware;

/// <summary>
/// Single place that converts exceptions to HTTP responses.
/// No try/catch blocks needed in controllers or services.
///
/// Add a new exception type here to handle it globally.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
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
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception ex)
    {
        var (statusCode, message, errors) = MapException(ex);

        // Log unhandled exceptions as errors; known validation issues as warnings
        if (statusCode >= 500)
            _logger.LogError(ex, "Unhandled exception. Path: {Path}", context.Request.Path);
        else
            _logger.LogWarning(ex, "Handled exception. Path: {Path}, Status: {Code}", context.Request.Path, statusCode);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var response = errors is not null
            ? ApiResponse.Fail(message, errors)
            : ApiResponse.Fail(message);

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response, JsonOptions)
        );
    }

    private static (int statusCode, string message, IEnumerable<string>? errors) MapException(Exception ex) =>
        ex switch
        {
            //FileValidationException fve =>
            //    (StatusCodes.Status400BadRequest,
            //     "File validation failed.",
            //     fve.ValidationResult.Errors.Select(e => e.ErrorMessage)),

            FileValidationException fve =>
            (
                StatusCodes.Status400BadRequest,
                "File validation failed.",
                new[]
                {
                    fve.ValidationResult.FailureReason ?? "Validation failed."
                }
            ),

            VirusDetectedException vde =>
                (StatusCodes.Status422UnprocessableEntity,
                 $"File rejected: {vde.ThreatName}",
                 null),

            VirusScanException vse =>
                (StatusCodes.Status503ServiceUnavailable,
                 vse.Message,
                 null),

            InvalidOperationException ioe =>
                (StatusCodes.Status400BadRequest,
                 ioe.Message,
                 null),

            _ =>
                (StatusCodes.Status500InternalServerError,
                 "An unexpected error occurred. Please try again later.",
                 null)
        };
}