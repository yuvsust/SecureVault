using SecureVault.Api.Models;
using System.Security;

namespace SecureVault.Api.Services;

public class FileService : IFileService
{
    // We will simulate a "Database" with a static list for now
    private static readonly List<StoredFile> _fileDb = new();
    private readonly string _storageDirectory = "VaultStorage";

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
        // TODO: Assignment 1 - Task 2
        // Ask Copilot to implement logic here.
        // It needs to: find file, create unique token, set ExpirationDate.
        throw new NotImplementedException();
    }
}