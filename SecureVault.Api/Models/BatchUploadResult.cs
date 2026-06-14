namespace SecureVault.Api.Models;

public class BatchUploadResult
{
    public string FileName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? FileId { get; set; }
}
