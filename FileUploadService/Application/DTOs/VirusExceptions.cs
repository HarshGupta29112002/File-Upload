namespace FileUploadService.Application.DTOs;

public class VirusDetectedException : Exception
{
    public string ThreatName { get; }

    public VirusDetectedException(string threatName)
        : base($"Virus detected: {threatName}")
    {
        ThreatName = threatName;
    }
}

public class VirusScanException : Exception
{
    public VirusScanException(string message) : base(message) { }
}