using SecureVault.Api.Models;

namespace SecureVault.Api.Services;

public interface IFileService
{
    // Task 1 will be to implement this
    Task<StoredFile> UploadFileAsync(IFormFile file);

    // Task 2 will be to implement this
    string GenerateShareLink(Guid fileId, double validHours);
}