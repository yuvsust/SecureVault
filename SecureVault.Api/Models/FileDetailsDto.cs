namespace SecureVault.Api.Models;

/// <summary>
/// Data Transfer Object for file details exposed via API.
/// Does NOT include internal storage path to prevent information disclosure.
/// </summary>
public class FileDetailsDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public bool HasShareLink => !string.IsNullOrEmpty(ShareToken);
    public string ShareToken { get; set; } = string.Empty;
}
