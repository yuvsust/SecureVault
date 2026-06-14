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
    // Batch upload limits
    private const int MAX_BATCH_FILES = 20;
    private const long MAX_FILE_BYTES = 50 * 1024 * 1024; // 50 MB per file
    private const long MAX_TOTAL_BYTES = 200 * 1024 * 1024; // 200 MB per batch

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

    public async Task<List<BatchUploadResult>> BatchUploadAsync(IEnumerable<IFormFile> files, CancellationToken ct = default)
    {
        var results = new List<BatchUploadResult>();
        var fileList = files?.ToList() ?? new List<IFormFile>();

        // Quick validations to avoid unnecessary work
        if (fileList.Count == 0)
            return results;

        if (fileList.Count > MAX_BATCH_FILES)
            throw new ArgumentException($"Too many files. Max allowed is {MAX_BATCH_FILES}.");

        long totalBytes = fileList.Sum(f => f.Length);
        if (totalBytes > MAX_TOTAL_BYTES)
            throw new ArgumentException("Total upload size exceeds allowed limit.");

        // Use a small degree of parallelism to improve throughput while limiting resource pressure
        var semaphore = new SemaphoreSlim(4);
        var tasks = new List<Task<BatchUploadResult>>();

        foreach (var file in fileList)
        {
            // Start a task per file but limit concurrency
            tasks.Add(Task.Run(async () =>
            {
                var result = new BatchUploadResult { FileName = file.FileName };
                await semaphore.WaitAsync(ct);
                try
                {
                    if (file.Length == 0 || file.Length > MAX_FILE_BYTES)
                    {
                        result.Success = false;
                        result.Message = "Invalid file size.";
                        return result;
                    }

                    var uploadedFile = await UploadFileAsync(file);
                    result.Success = true;
                    result.Message = "File uploaded successfully.";
                    result.FileId = uploadedFile.Id;
                    return result;
                }
                catch (OperationCanceledException)
                {
                    return new BatchUploadResult { FileName = file.FileName, Success = false, Message = "Cancelled" };
                }
                catch
                {
                    // Don't leak internal exception messages to clients
                    return new BatchUploadResult { FileName = file.FileName, Success = false, Message = "Upload failed" };
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct));
        }
        var completed = await Task.WhenAll(tasks);
        results.AddRange(completed);
        return results;
    }

    public async Task<ShareLinkExtensionResult> ExtendShareLinkAsync(Guid fileId, double additionalHours, string shareToken)
    {
        if (additionalHours <= 0)
            throw new ArgumentException("Additional hours must be positive.", nameof(additionalHours));

        if (string.IsNullOrWhiteSpace(shareToken))
            throw new ArgumentException("Share token is required to extend.", nameof(shareToken));

        if (!_fileDb.TryGetValue(fileId, out var storedFile))
            throw new KeyNotFoundException($"File with ID {fileId} not found.");

        // Verify share token matches
        if (!string.Equals(storedFile.ShareToken, shareToken, StringComparison.Ordinal))
        {
            return new ShareLinkExtensionResult { Success = false, Message = "Invalid share token.", FileId = fileId };
        }

        // If already expired, do not allow extension
        if (storedFile.ExpirationDate == null || storedFile.ExpirationDate < DateTime.UtcNow)
        {
            return new ShareLinkExtensionResult { Success = false, Message = "Share link expired and cannot be extended.", FileId = fileId };
        }

        // Enforce maximum total expiration horizon
        var newExpiration = storedFile.ExpirationDate.Value.AddHours(additionalHours);
        var maxAllowed = DateTime.UtcNow.AddHours(MAX_VALID_HOURS);
        if (newExpiration > maxAllowed)
        {
            return new ShareLinkExtensionResult { Success = false, Message = "Cannot extend beyond maximum allowed expiration.", FileId = fileId };
        }

        // Concurrency: lock on storedFile instance for thread-safety
        lock (storedFile)
        {
            storedFile.ExpirationDate = newExpiration;
        }

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