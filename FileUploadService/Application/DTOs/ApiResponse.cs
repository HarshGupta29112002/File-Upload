namespace FileUploadService.Application.DTOs;

/// <summary>
/// Standard API response envelope used by all endpoints.
/// Ensures every response has a consistent shape:
///   { success, message, data, errors }
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
    public IEnumerable<string>? Errors { get; init; }

    public static ApiResponse<T> Ok(T data, string message = "Success") =>
        new() { Success = true, Message = message, Data = data };

    public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}

/// <summary>
/// Non-generic version for responses with no data payload (e.g. delete).
/// </summary>
public class ApiResponse
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IEnumerable<string>? Errors { get; init; }

    public static ApiResponse Ok(string message = "Success") =>
        new() { Success = true, Message = message };

    public static ApiResponse Fail(string message, IEnumerable<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}