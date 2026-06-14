namespace SecureVault.Api.Models;

public class ShareLinkExtensionResult
{
    public Guid FileId { get; set; }
    public string ShareToken { get; set; } = string.Empty;
    public DateTime? NewExpirationDate { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
