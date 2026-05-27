using System.Text;

namespace FileUploadService.Middleware;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _logBasePath;

    public RequestLoggingMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        var configuredPath = configuration["LogSettings:BasePath"] ?? "Logs";
        _logBasePath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(Directory.GetCurrentDirectory(), configuredPath);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var now = DateTime.Now;

        var basePath = Path.Combine(
            _logBasePath,
            now.Year.ToString(),
            now.ToString("MM"),
            now.ToString("dd")
        );

        var requestPath = Path.Combine(basePath, "request");
        var responsePath = Path.Combine(basePath, "response");
        var errorPath = Path.Combine(basePath, "error");
        var appPath = Path.Combine(basePath, "application");

        Directory.CreateDirectory(requestPath);
        Directory.CreateDirectory(responsePath);
        Directory.CreateDirectory(errorPath);
        Directory.CreateDirectory(appPath);

        // GUID suffix so concurrent requests never collide on the same filename
        var fileName = $"{now:HHmmssfff}_{Guid.NewGuid():N}.txt";

        var correlationId = context.Items[CorrelationIdMiddleware.HeaderName]?.ToString() ?? "N/A";

        context.Request.EnableBuffering();

        string requestBody = "(none)";
        var contentType = context.Request.ContentType ?? string.Empty;
        bool isMultipart = contentType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase);

        if (!isMultipart && context.Request.ContentLength > 0)
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
        }
        else if (isMultipart)
        {
            requestBody = "(multipart/form-data - body not logged)";
        }

        var requestLog = $@"
Time:          {now}
CorrelationId: {correlationId}
Method:        {context.Request.Method}
Path:          {context.Request.Path}
QueryString:   {context.Request.QueryString}
ContentType:   {context.Request.ContentType}
Body:          {requestBody}
";
        await File.WriteAllTextAsync(Path.Combine(requestPath, fileName), requestLog);

        var appStartLog = $@"
Time:          {now}
CorrelationId: {correlationId}
Message:       Request started
Path:          {context.Request.Path}
Method:        {context.Request.Method}
";
        await File.AppendAllTextAsync(Path.Combine(appPath, fileName), appStartLog);

        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);

            responseBody.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(responseBody).ReadToEndAsync();

            var responseLog = $@"
Time:          {DateTime.Now}
CorrelationId: {correlationId}
StatusCode:    {context.Response.StatusCode}
Path:          {context.Request.Path}
Response:      {responseText}
";
            await File.WriteAllTextAsync(Path.Combine(responsePath, fileName), responseLog);

            if (context.Response.StatusCode >= 400)
            {
                var errorLog = $@"
Time:          {DateTime.Now}
CorrelationId: {correlationId}
Message:       HTTP Error
StatusCode:    {context.Response.StatusCode}
Path:          {context.Request.Path}
Response:      {responseText}
";
                await File.WriteAllTextAsync(Path.Combine(errorPath, fileName), errorLog);
            }

            var appSuccessLog = $@"
Time:          {DateTime.Now}
CorrelationId: {correlationId}
Message:       Request completed
StatusCode:    {context.Response.StatusCode}
Path:          {context.Request.Path}
";
            await File.AppendAllTextAsync(Path.Combine(appPath, fileName), appSuccessLog);

            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        catch (Exception ex)
        {
            var errorLog = $@"
Time:          {DateTime.Now}
CorrelationId: {correlationId}
Message:       Exception occurred
Error:         {ex.Message}
Path:          {context.Request.Path}
StackTrace:    {ex.StackTrace}
";
            await File.WriteAllTextAsync(Path.Combine(errorPath, fileName), errorLog);

            var appErrorLog = $@"
Time:          {DateTime.Now}
CorrelationId: {correlationId}
Message:       Request failed with exception
Error:         {ex.Message}
Path:          {context.Request.Path}
";
            await File.AppendAllTextAsync(Path.Combine(appPath, fileName), appErrorLog);

            context.Response.Body = originalBodyStream;

            throw;
        }
    }
}