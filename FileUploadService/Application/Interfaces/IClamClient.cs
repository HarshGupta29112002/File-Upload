using FileUploadService.Application.DTOs;

namespace FileUploadService.Application.Interfaces;

public interface IVirusScanClient
{
    Task<bool> PingAsync();
    Task<VirusScanResult> ScanAsync(Stream fileStream, string fileName);
}

public interface IClamClientFactory
{
    IVirusScanClient Create(string host, int port);
}