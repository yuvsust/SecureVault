using System;
using System.IO;
using System.Threading;
using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Threading;
using Microsoft.AspNetCore.Http;
using SecureVault.Api.Models;

namespace SecureVault.Api.Services;

public interface IFileService
{
    // Task 1 will be to implement this
    Task<StoredFile> UploadFileAsync(IFormFile file);

    // Task 2 will be to implement this
    string GenerateShareLink(Guid fileId, double validHours);

    // Download a file stream for a given share token. Returns a tuple of stream and original filename.
    Task<(Stream Stream, string FileName)> DownloadFileAsync(string shareToken, CancellationToken ct);

    // Delete a file by id. Returns true if deleted, false if not found.
    Task<bool> DeleteFileAsync(Guid fileId);

    // Returns file details DTO (safe for API response, no internal paths exposed).
    Task<FileDetailsDto> GetFileDetailsAsync(Guid fileId);

    // Batch upload multiple files and return results with individual status.
    Task<List<BatchUploadResult>> BatchUploadAsync(IFormCollection files);
}