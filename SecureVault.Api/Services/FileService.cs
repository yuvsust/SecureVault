using SecureVault.Api.Models;
using System.Security;

namespace SecureVault.Api.Services;

public class FileService : IFileService
{
    // We will simulate a "Database" with a static list for now
    private static readonly List<StoredFile> _fileDb = new();
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
        using (var fileStream = new FileStream(storedPath, FileMode.Create, FileAccess.Write, FileShare.None))
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

        // Add to static list
        _fileDb.Add(storedFile);

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
        var file = _fileDb.FirstOrDefault(f => f.Id == fileId);
        if (file == null)
            throw new KeyNotFoundException($"File with ID {fileId} not found.");

        // Generate secure token
        var shareToken = Guid.NewGuid().ToString("N").Substring(0, TOKEN_LENGTH);

        // Update file metadata
        file.ShareToken = shareToken;
        file.ExpirationDate = DateTime.UtcNow.AddHours(validHours);

        // Return share link
        return $"{BASE_URL}/api/files/download/{shareToken}";
    }
}