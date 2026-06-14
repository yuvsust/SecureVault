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
}