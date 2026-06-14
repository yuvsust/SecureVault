using SecureVault.Api.Models;
using System.Security;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace SecureVault.Api.Services;

public class FileService : IFileService
{
    // Thread-safe in-memory stores
    private static readonly ConcurrentDictionary<Guid, StoredFile> _fileDb = new();
    // token -> fileId index for fast lookup
    private static readonly ConcurrentDictionary<string, Guid> _tokenIndex = new();
    private readonly string _storageDirectory = "VaultStorage";

    // Constants for share link generation
    private const int MAX_VALID_HOURS = 8760; // 1 year
    private const int TOKEN_LENGTH = 32;
    private const string BASE_URL = "https://api.securevault.com";

    public FileService()
    {
        // Ensure storage directory exists
        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }
    }

    public async Task<StoredFile> UploadFileAsync(IFormFile file)
    {
        // Validate input
        if (file == null || file.Length == 0)
            throw new ArgumentException("File cannot be null or empty.", nameof(file));

        // Generate unique identifier and safe filename
        var fileId = Guid.NewGuid();
        var storedFileName = $"{fileId}";
        var storedPath = Path.Combine(_storageDirectory, storedFileName);

        // Prevent path traversal by verifying the final path is within storage directory
        var fullPath = Path.GetFullPath(storedPath);
        var fullStorageDir = Path.GetFullPath(_storageDirectory);
        if (!fullPath.StartsWith(fullStorageDir, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("Invalid file path detected.");

        // Save file to disk using FileStream with async I/O
        using (var fileStream = new FileStream(storedPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await file.CopyToAsync(fileStream);
        }

        // Create metadata entry
        var storedFile = new StoredFile
        {
            Id = fileId,
            FileName = file.FileName,
            StoredPath = storedPath,
            UploadedAt = DateTime.UtcNow
        };

        // Add to thread-safe dictionary
        _fileDb[storedFile.Id] = storedFile;

        return storedFile;
    }

    public string GenerateShareLink(Guid fileId, double validHours)
    {
        // Validate hours
        if (validHours <= 0)
            throw new ArgumentException("Valid hours must be greater than 0.", nameof(validHours));

        if (validHours > MAX_VALID_HOURS)
            throw new ArgumentException($"Valid hours cannot exceed {MAX_VALID_HOURS} hours (1 year).", nameof(validHours));

        // Find file
        if (!_fileDb.TryGetValue(fileId, out var file))
            throw new KeyNotFoundException($"File with ID {fileId} not found.");

        // Generate secure token and ensure uniqueness
        string shareToken;
        do
        {
            // Use RNG to create a URL-safe token
            var bytes = new byte[24]; // 24 bytes -> 32+ base64 chars
            RandomNumberGenerator.Fill(bytes);
            shareToken = Convert.ToBase64String(bytes).TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_')
                .Substring(0, TOKEN_LENGTH);
        } while (_tokenIndex.ContainsKey(shareToken));

        // Update file metadata and indexes
        file.ShareToken = shareToken;
        file.ExpirationDate = DateTime.UtcNow.AddHours(validHours);
        _tokenIndex[shareToken] = file.Id;

        // Return share link
        return $"{BASE_URL}/api/files/download/{shareToken}";
    }

    public async Task<(Stream Stream, string FileName)> DownloadFileAsync(string shareToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(shareToken))
            throw new ArgumentException("shareToken is required", nameof(shareToken));

        // Lookup token -> fileId
        if (!_tokenIndex.TryGetValue(shareToken, out var fileId))
            throw new KeyNotFoundException("Share token not found or expired.");

        // Lookup file metadata
        if (!_fileDb.TryGetValue(fileId, out var storedFile))
            throw new KeyNotFoundException("File metadata not found.");

        // Check expiration
        if (storedFile.ExpirationDate == null || storedFile.ExpirationDate < DateTime.UtcNow)
            throw new KeyNotFoundException("Share token expired.");

        // Ensure file exists
        if (!File.Exists(storedFile.StoredPath))
            throw new KeyNotFoundException("File not found on disk.");

        // Open async read stream and return with original filename
        var fs = new FileStream(storedFile.StoredPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

        return (fs, storedFile.FileName);
    }

    public async Task<bool> DeleteFileAsync(Guid fileId)
    {
        // Atomically remove from in-memory store
        if (!_fileDb.TryRemove(fileId, out var storedFile))
            return false;

        // Remove token index if present
        if (!string.IsNullOrEmpty(storedFile.ShareToken))
            _tokenIndex.TryRemove(storedFile.ShareToken, out _);

        // Delete file from disk if exists
        try
        {
            if (File.Exists(storedFile.StoredPath))
            {
                File.Delete(storedFile.StoredPath);
            }
        }
        catch
        {
            // If disk delete fails, we already removed metadata; rethrow to let caller decide
            throw;
        }

        await Task.CompletedTask;
        return true;
    }

    public Task<FileDetailsDto> GetFileDetailsAsync(Guid fileId)
    {
        if (!_fileDb.TryGetValue(fileId, out var storedFile))
            throw new KeyNotFoundException($"File with ID {fileId} not found.");

        // Return DTO that excludes internal StoredPath to prevent information disclosure.
        var dto = new FileDetailsDto
        {
            Id = storedFile.Id,
            FileName = storedFile.FileName,
            UploadedAt = storedFile.UploadedAt,
            ExpirationDate = storedFile.ExpirationDate,
            ShareToken = storedFile.ShareToken
        };

        return Task.FromResult(dto);
    }

    public async Task<List<BatchUploadResult>> BatchUploadAsync(IFormCollection files)
    {
        var results = new List<BatchUploadResult>();

        // FLAW 1: No atomic transaction. If one file fails mid-way, previous files are already in the store.
        // FLAW 2: Missing per-file size validation before upload starts.
        // FLAW 3: Serial upload instead of parallel (inefficient for multiple large files).
        
        foreach (var file in files.Files)
        {
            var result = new BatchUploadResult { FileName = file.FileName };

            try
            {
                // FLAW: No pre-check for file size, empty files, or other issues.
                var uploadedFile = await UploadFileAsync(file);
                result.Success = true;
                result.Message = "File uploaded successfully.";
                result.FileId = uploadedFile.Id;
            }
            catch (Exception ex)
            {
                // FLAW 3: Generic error message. Should provide per-file feedback.
                result.Success = false;
                result.Message = ex.Message; // Potentially leaks internal details
                result.FileId = null;
            }

            results.Add(result);
        }

        return results;
    }

    public async Task<ShareLinkExtensionResult> ExtendShareLinkAsync(Guid fileId, double additionalHours)
    {
        if (additionalHours <= 0)
            throw new ArgumentException("Additional hours must be positive.", nameof(additionalHours));

        if (!_fileDb.TryGetValue(fileId, out var storedFile))
            throw new KeyNotFoundException($"File with ID {fileId} not found.");

        if (storedFile.ExpirationDate == null)
            return new ShareLinkExtensionResult
            {
                Success = false,
                Message = "Share link is not active and cannot be extended.",
                FileId = fileId
            };

        // FLAW: No permission or ownership check.
        // FLAW: Does not enforce a maximum total expiration horizon; token can be extended indefinitely.
        // FLAW: Potential race if another request deletes or updates the token concurrently.
        storedFile.ExpirationDate = storedFile.ExpirationDate.Value.AddHours(additionalHours);

        return new ShareLinkExtensionResult
        {
            Success = true,
            Message = "Share link extended.",
            FileId = fileId,
            ShareToken = storedFile.ShareToken,
            NewExpirationDate = storedFile.ExpirationDate
        };
    }
}