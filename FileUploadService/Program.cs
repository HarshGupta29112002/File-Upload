using FileUploadService.Application.Configurations;
using FileUploadService.Application.Implementation;
using FileUploadService.Application.Interfaces;
using FileUploadService.Middleware;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// ── MVC ───────────────────────────────────────────────────────
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .SelectMany(e => e.Value!.Errors.Select(err =>
                    $"{e.Key}: {(string.IsNullOrWhiteSpace(err.ErrorMessage)
                        ? "Invalid value." : err.ErrorMessage)}"
                )).ToList();

            return new BadRequestObjectResult(new
            {
                success = false,
                message = "Request validation failed.",
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
        Version = "v1"
    });
});

// ── CONFIGURATION ─────────────────────────────────────────────
builder.Services.Configure<FileStorageSettings>(
    builder.Configuration.GetSection(FileStorageSettings.SectionName)
);
builder.Services.Configure<VideoStorageSettings>(
    builder.Configuration.GetSection(VideoStorageSettings.SectionName)
);
builder.Services.Configure<ClamAvSettings>(
    builder.Configuration.GetSection(ClamAvSettings.SectionName)
);

// ── KESTREL / FORM LIMITS ─────────────────────────────────────
builder.WebHost.ConfigureKestrel(options =>
{
    // 500 MB — must cover the largest video upload you expect
    options.Limits.MaxRequestBodySize = 524_288_000;
});

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524_288_000; // 500 MB
    options.ValueLengthLimit = 1_048_576;
    options.MultipartHeadersLengthLimit = 16_384;
});

// ── SHARED SERVICES ───────────────────────────────────────────
builder.Services.AddSingleton<IClamClientFactory, DefaultClamClientFactory>();
builder.Services.AddScoped<VirusScanService>();
builder.Services.AddScoped<FileValidationService>();

// Storage abstraction — swap for MinioStorageService later in one line
builder.Services.AddScoped<IStorageService, LocalStorageService>();
builder.Services.AddScoped<IFileRepository, FileRepository>();

// ── DOCUMENT SERVICES ─────────────────────────────────────────
builder.Services.AddScoped<IFileService, FileService>();

// ── VIDEO SERVICES ────────────────────────────────────────────
builder.Services.AddScoped<VideoValidationService>();
builder.Services.AddSingleton<FfprobeService>();   // stateless — singleton is fine
builder.Services.AddScoped<IVideoService, VideoService>();

// ── BUILD ─────────────────────────────────────────────────────
var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "File Upload Service v1");
    options.RoutePrefix = string.Empty;
});

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.MapControllers();

// Ensure both storage folders exist at startup
var docUploads = Path.Combine(
    Directory.GetCurrentDirectory(),
    builder.Configuration["FileStorage:BasePath"] ?? "uploads"
);
var videoUploads = Path.Combine(
    Directory.GetCurrentDirectory(),
    builder.Configuration["VideoStorage:BasePath"] ?? "uploads/videos"
);
Directory.CreateDirectory(docUploads);
Directory.CreateDirectory(videoUploads);

app.Run();

public partial class Program { }