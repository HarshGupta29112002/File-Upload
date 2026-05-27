using MimeDetective;
using MimeDetective.Storage;
using Microsoft.Extensions.Options;
using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;

namespace FileUploadService.Application.Implementation;

public class FileValidationService
{
    private readonly FileStorageSettings _settings;
    private readonly ILogger<FileValidationService> _logger;

    private readonly ContentInspector _inspector;

    public FileValidationService(
        IOptions<FileStorageSettings> settings,
        ILogger<FileValidationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        _inspector = new ContentInspectorBuilder()
        {
            Definitions = MimeDetective.Definitions.Default.All()
        }.Build();
    }


    //  MAIN ENTRY POINT
    public async Task<FileValidationResult> ValidateAsync(IFormFile file)
    {
        var result = new FileValidationResult
        {
            Details = new ValidationDetails
            {
                FileSizeBytes = file.Length,
                ClaimedMimeType = file.ContentType,
                ClaimedExtension = Path.GetExtension(file.FileName).ToLowerInvariant()
            }
        };

        _logger.LogInformation(
            "Starting validation. File: {Name}, Size: {Size}, ContentType: {CT}",
            file.FileName, file.Length, file.ContentType
        );

        // LAYER 0: File size 
        var sizeCheck = ValidateFileSize(file.Length);
        if (!sizeCheck.IsValid)
        {
            result.FailureReason = sizeCheck.FailureReason;
            return result;
        }

        // LAYER 1: Extension
        var extCheck = ValidateExtension(file.FileName);
        if (!extCheck.IsValid)
        {
            result.FailureReason = extCheck.FailureReason;
            return result;
        }

        // LAYER 2: MIME type (optional)
        var mimeCheck = ValidateMimeType(file.ContentType, result.Details.ClaimedExtension!);
        if (!mimeCheck.IsValid)
        {
            result.FailureReason = mimeCheck.FailureReason;
            return result;
        }

        // LAYER 3: Magic bytes
        var magicCheck = await ValidateMagicBytesAsync(file, result.Details);
        if (!magicCheck.IsValid)
        {
            result.FailureReason = magicCheck.FailureReason;
            return result;
        }

        // CROSS-CHECK
        var crossCheck = CrossCheckResults(result.Details);
        if (!crossCheck.IsValid)
        {
            result.FailureReason = crossCheck.FailureReason;
            return result;
        }

        // All layers passed and are consistent
        result.IsValid = true;
        _logger.LogInformation("Validation passed for file: {Name}", file.FileName);
        return result;
    }


    //  LAYER 0: FILE SIZE
    private FileValidationResult ValidateFileSize(long fileSizeBytes)
    {

        if (fileSizeBytes <= 0)
            return Fail("File is empty.");

        if (fileSizeBytes > _settings.MaxFileSizeBytes)
            return Fail(
                $"File size {fileSizeBytes:N0} bytes exceeds the maximum allowed size of {_settings.MaxFileSizeBytes:N0} bytes (10 MB)."
            );

        return Pass();
    }

    //  LAYER 1: EXTENSION VALIDATION
    private FileValidationResult ValidateExtension(string filename)
    { 

        if (string.IsNullOrWhiteSpace(filename))
            return Fail("Filename is missing or empty.");

        var safeFilename = Path.GetFileName(filename);

        var lastExtension = Path.GetExtension(safeFilename).ToLowerInvariant();
        var nameWithoutLastExt = Path.GetFileNameWithoutExtension(safeFilename);
        var secondExtension = Path.GetExtension(nameWithoutLastExt);

        if (!string.IsNullOrEmpty(secondExtension))
        {
            _logger.LogWarning(
                "Double extension attack attempt. Filename: {Filename}", filename
            );
            return Fail(
                $"Filename '{safeFilename}' contains multiple extensions. This is not allowed."
            );
        }

        if (string.IsNullOrEmpty(lastExtension))
            return Fail("File has no extension.");

        if (!_settings.AllowedExtensions.Contains(lastExtension))
        {
            _logger.LogWarning(
                "Blocked extension: {Extension}. Filename: {Filename}",
                lastExtension, filename
            );
            return Fail(
                $"File extension '{lastExtension}' is not allowed. " +
                $"Allowed: {string.Join(", ", _settings.AllowedExtensions)}"
            );
        }

        return Pass();
    }


    //  LAYER 2: MIME TYPE VALIDATION (OPTIONAL)
    private FileValidationResult ValidateMimeType(string? contentType, string claimedExtension)
    {

        if (string.IsNullOrWhiteSpace(contentType) || contentType == "application/octet-stream")
        {
            
            _logger.LogInformation("MIME type absent or generic — skipping MIME validation.");
            return Pass();
        }

        var normalizedMime = contentType.Split(';')[0].Trim().ToLowerInvariant();

        if (!_settings.AllowedMimeTypes.TryGetValue(claimedExtension, out var expectedMime))
        {

            _logger.LogWarning(
                "No MIME mapping found for extension {Ext} — skipping MIME check.",
                claimedExtension
            );
            return Pass();
        }

        if (normalizedMime != expectedMime)
        {
            _logger.LogWarning(
                "MIME mismatch. Extension: {Ext}, Expected MIME: {Expected}, Got: {Got}",
                claimedExtension, expectedMime, normalizedMime
            );
            return Fail(
                $"MIME type mismatch. For extension '{claimedExtension}', " +
                $"expected '{expectedMime}' but received '{normalizedMime}'."
            );
        }

        return Pass();
    }

    //  LAYER 3: MAGIC BYTE VALIDATION
    private async Task<FileValidationResult> ValidateMagicBytesAsync(
        IFormFile file,
        ValidationDetails details)
    {
        
        const int signatureBytesLength = 560;
        var headerBuffer = new byte[signatureBytesLength];

        await using var stream = file.OpenReadStream();
        var bytesRead = await stream.ReadAsync(headerBuffer, 0, signatureBytesLength);

     
        if (bytesRead == 0)
            return Fail("File appears to be empty — no bytes could be read.");

    
        var headerBytes = headerBuffer[..bytesRead];

      
        var inspectionResults = _inspector.Inspect(headerBytes);
        var topMatch = inspectionResults
            .OrderByDescending(r => r.Points)
            .FirstOrDefault();

        if (topMatch is null)
        {
            
            _logger.LogWarning(
                "Magic byte inspection returned no match. Filename: {Name}", file.FileName
            );
            return Fail("File type could not be identified from its content. Upload rejected for security.");
        }

   
        var detectedExtension = topMatch.Definition.File.Extensions
            .FirstOrDefault()?.ToLowerInvariant();

        if (detectedExtension is not null && !detectedExtension.StartsWith('.'))
            detectedExtension = "." + detectedExtension;

        details.DetectedFileType = detectedExtension ?? "unknown";

        _logger.LogInformation(
            "Magic byte detection: {Detected} (confidence points: {Points})",
            detectedExtension, topMatch.Points
        );

        if (detectedExtension is null ||
            !_settings.AllowedExtensions.Contains(detectedExtension))
        {
            _logger.LogWarning(
                "Magic byte detected disallowed type: {Detected}. Filename: {Name}",
                detectedExtension, file.FileName
            );
            return Fail(
                $"File content inspection detected type '{detectedExtension}' " +
                $"which is not in the allowed file types list."
            );
        }

        return Pass();
    }

   
    //  CROSS-CHECK: ALL LAYERS MUST AGREE
    private static FileValidationResult CrossCheckResults(ValidationDetails details)
    {
        var claimedExt = NormalizeExtension(details.ClaimedExtension);
        var detectedExt = NormalizeExtension(details.DetectedFileType);

        var zipBasedFormats = new HashSet<string> { ".docx", ".xlsx", ".zip" };
        if (zipBasedFormats.Contains(claimedExt ?? "") && detectedExt == ".zip")
            return Pass(); 

        if (claimedExt != detectedExt)
        {
            return new FileValidationResult
            {
                IsValid = false,
                FailureReason = $"File content does not match its extension. " +
                                $"Extension claims '{details.ClaimedExtension}' " +
                                $"but content inspection detected '{details.DetectedFileType}'."
            };
        }

        return Pass();
    }


    //  HELPERS

    private static string? NormalizeExtension(string? ext)
    {
        if (ext is null) return null;
        var lower = ext.ToLowerInvariant();
        return lower == ".jpg" ? ".jpeg" : lower;
    }

    private static FileValidationResult Pass() =>
        new() { IsValid = true };

    private static FileValidationResult Fail(string reason) =>
        new() { IsValid = false, FailureReason = reason };
}