namespace SecureVault.Api.Models;

public class StoredFile
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoredPath { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public string ShareToken { get; set; } = string.Empty;
}