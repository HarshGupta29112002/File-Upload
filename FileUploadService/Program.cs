using FileUploadService.Application.Configurations;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FileUploadService.Middleware;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(err =>
                    $"{e.Key}: {(string.IsNullOrWhiteSpace(err.ErrorMessage)
                        ? "Invalid value provided."
                        : err.ErrorMessage)}"
                ))
                .ToList();

            var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.HeaderName]
                                    ?.ToString() ?? "N/A";

            return new BadRequestObjectResult(new
            {
                success = false,
                correlationId,
                message = "Request validation failed. See 'errors' for details.",
                errors
            });
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "File Upload Microservice",
        Version = "v1",
        Description = "A minimal microservice for uploading and downloading files."
    });
});

builder.Services.Configure<FileStorageSettings>(
    builder.Configuration.GetSection(FileStorageSettings.SectionName)
);

builder.Services.Configure<ClamAvSettings>(
    builder.Configuration.GetSection(ClamAvSettings.SectionName)
);

builder.Services.Configure<EncryptionSettings>(
    builder.Configuration.GetSection(EncryptionSettings.SectionName)
);

// Change 4: validate AES key at startup — fail fast rather than fail on first upload.
// This calls GetKeyBytes() which throws a clear InvalidOperationException if
// Encryption:AesKey is missing, not Base64, or not 32 bytes.
var encryptionSettings = builder.Configuration
    .GetSection(EncryptionSettings.SectionName)
    .Get<EncryptionSettings>()
    ?? throw new InvalidOperationException(
        "Encryption section is missing from configuration entirely."
    );
encryptionSettings.GetKeyBytes(); // throws immediately if key is wrong

builder.Services.AddSingleton<IClamClientFactory, DefaultClamClientFactory>();
builder.Services.AddScoped<VirusScanService>();
builder.Services.AddScoped<EncryptionService>();
builder.Services.AddScoped<FileValidationService>();
builder.Services.AddScoped<IFileService, FileService>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 52_428_800; // 50 MB
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 52_428_800; // 50 MB
    options.ValueLengthLimit = 1_048_576;
    options.MultipartHeadersLengthLimit = 16_384;
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "File Upload Service v1");
    options.RoutePrefix = string.Empty;
});

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapControllers();

var uploadsPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    builder.Configuration["FileStorage:BasePath"] ?? "uploads"
);
Directory.CreateDirectory(uploadsPath);

app.Run();

public partial class Program { } // for xunit integration testing