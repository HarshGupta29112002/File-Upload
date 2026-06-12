namespace FileUploadService.Application.DTOs
{
    public class VideoUploadRequest
    {
        public IFormFile File { get; set; } = null!;
        public string? UploadedBy { get; set; }
    }
}
