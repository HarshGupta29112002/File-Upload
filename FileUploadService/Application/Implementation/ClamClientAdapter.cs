using FileUploadService.Application.DTOs;
using FileUploadService.Application.Interfaces;
using nClam;

namespace FileUploadService.Application.Implementation;

public class DefaultClamClientFactory : IClamClientFactory
{
    public IVirusScanClient Create(string host, int port)
        => new ClamClientAdapter(host, port);
}

public class ClamClientAdapter : IVirusScanClient
{
    private readonly ClamClient _inner;

    public ClamClientAdapter(string host, int port)
    {
        _inner = new ClamClient(host, port)
        {
            MaxStreamSize = 26_214_400
        };
    }

    public Task<bool> PingAsync()
        => _inner.PingAsync();

    public async Task<VirusScanResult> ScanAsync(Stream fileStream, string fileName)
    {
        var scanResult = await _inner.SendAndScanFileAsync(fileStream);

        return scanResult.Result switch
        {
            ClamScanResults.Clean => VirusScanResult.Clean(),
            ClamScanResults.VirusDetected => VirusScanResult.Infected(
                scanResult.InfectedFiles?.FirstOrDefault()?.VirusName ?? "Unknown threat"),
            _ => VirusScanResult.Unavailable()
        };
    }
}