using FileUploadService.Application.Configurations;
using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using Microsoft.Extensions.Options;

namespace FileUploadService.Application.Implementation;

public class VirusScanService
{
    private readonly ClamAvSettings _settings;
    private readonly ILogger<VirusScanService> _logger;
    private readonly IClamClientFactory _clamClientFactory;

    public VirusScanService(
        IOptions<ClamAvSettings> settings,
        ILogger<VirusScanService> logger,
        IClamClientFactory clamClientFactory)
    {
        _settings = settings.Value;
        _logger = logger;
        _clamClientFactory = clamClientFactory;
    }

    public async Task<VirusScanResult> ScanAsync(IFormFile file)
    {
        _logger.LogInformation(
            "Starting virus scan. File: {Name}, Size: {Size} bytes",
            file.FileName, file.Length);

        try
        {
            var clam = _clamClientFactory.Create(_settings.Host, _settings.Port);

            var pingResult = await clam.PingAsync();
            if (!pingResult)
            {
                _logger.LogError(
                    "ClamAV ping failed — daemon not responding on {Host}:{Port}",
                    _settings.Host, _settings.Port);
                return VirusScanResult.Unavailable();
            }

            await using var stream = file.OpenReadStream();
            var result = await clam.ScanAsync(stream, file.FileName);

            if (result.ScannerUnavailable)
            {
                _logger.LogError("ClamAV returned an error result for file: {Name}", file.FileName);
                return VirusScanResult.Unavailable();
            }

            if (!result.IsClean)
            {
                _logger.LogWarning(
                    "Virus detected in file: {Name}. Threat: {Threat}",
                    file.FileName, result.ThreatName);
                return VirusScanResult.Infected(result.ThreatName ?? "Unknown threat");
            }

            _logger.LogInformation("Virus scan passed. File: {Name}", file.FileName);
            return VirusScanResult.Clean();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ClamAV scan threw an exception for file: {Name}", file.FileName);
            return VirusScanResult.Unavailable();
        }
    }
}