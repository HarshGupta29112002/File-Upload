namespace FileUploadService.Application.DTOs;

public class VirusScanResult
{
    public bool IsClean { get; set; }

    public string? ThreatName { get; set; }

    public bool ScannerUnavailable { get; set; }

    public static VirusScanResult Clean() =>
        new() { IsClean = true };

    public static VirusScanResult Infected(string threatName) =>
        new() { IsClean = false, ThreatName = threatName };

    public static VirusScanResult Unavailable() =>
        new() { IsClean = false, ScannerUnavailable = true };
}