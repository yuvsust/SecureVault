using SecureVault.Api.Models;
using SecureVault.Api.Services;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Http;
using System.Threading;
using System.IO;

namespace SecureVault.Tests;

public class FileServiceTests
{
    private readonly FileService _fileService;

    public FileServiceTests()
    {
        _fileService = new FileService();
    }

    #region Helper Methods

    /// <summary>
    /// Creates a mock IFormFile with consistent setup.
    /// </summary>
    private Mock<IFormFile> CreateMockFile(string fileName = "test.txt", int fileSize = 100)
    {
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns(fileName);
        mockFile.Setup(f => f.Length).Returns(fileSize);
        // Use lambda to ensure each call returns a fresh MemoryStream
        mockFile.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(new byte[fileSize]));
        return mockFile;
    }

    /// <summary>
    /// Uploads a file and returns the StoredFile object.
    /// </summary>
    private async Task<StoredFile> UploadTestFileAsync(string fileName = "test.txt")
    {
        var mockFile = CreateMockFile(fileName);
        return await _fileService.UploadFileAsync(mockFile.Object);
    }

    #endregion

    #region Download/Delete Tests

    [Fact]
    public async Task Download_ExpiredToken_ThrowsKeyNotFoundException()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync();
        var link = _fileService.GenerateShareLink(uploadedFile.Id, 24);
        var token = link.Split('/').Last();

        // Simulate expiration
        uploadedFile.ExpirationDate = DateTime.UtcNow.AddSeconds(-1);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await _fileService.DownloadFileAsync(token, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Delete_RemovesFile_AndDownloadFails()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync("toDelete.txt");
        var link = _fileService.GenerateShareLink(uploadedFile.Id, 24);
        var token = link.Split('/').Last();

        // Act - delete the file
        var deleted = await _fileService.DeleteFileAsync(uploadedFile.Id);

        // Assert delete succeeded
        Assert.True(deleted);

        // Download should now fail
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
        {
            await _fileService.DownloadFileAsync(token, CancellationToken.None);
        });
    }

    [Fact]
    public async Task Delete_NonExistent_ReturnsFalse()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var result = await _fileService.DeleteFileAsync(nonExistentId);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetFileDetails_ReturnsStoredFileWithInternalPath_ExposesBug()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync("leak.txt");

        // Act
        var details = await _fileService.GetFileDetailsAsync(uploadedFile.Id);

        // Assert - DTO should NOT expose internal StoredPath anymore
        Assert.Equal(uploadedFile.Id, details.Id);
        Assert.Equal(uploadedFile.FileName, details.FileName);
        // FIXED: FileDetailsDto does not include StoredPath, preventing information disclosure.
        Assert.NotNull(details.FileName);
        Assert.True(details.UploadedAt > DateTime.MinValue);
    }

    #endregion

    #region Batch Upload Tests

    [Fact]
    public async Task BatchUploadAsync_WithMultipleFiles_ReturnsAllResults()
    {
        // Arrange
        var mockFormCollection = new Mock<IFormCollection>();
        var files = new List<IFormFile>
        {
            CreateMockFile("file1.txt").Object,
            CreateMockFile("file2.txt").Object,
            CreateMockFile("file3.txt").Object
        };
        mockFormCollection.Setup(fc => fc.Files).Returns(new FormFileCollection { files[0], files[1], files[2] });

        // Act
        var results = await _fileService.BatchUploadAsync(mockFormCollection.Object);

        // Assert
        Assert.NotEmpty(results);
        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.True(r.Success));
    }

    [Fact]
    public async Task BatchUploadAsync_WithEmptyCollection_ReturnsEmptyResults()
    {
        // Arrange
        var mockFormCollection = new Mock<IFormCollection>();
        mockFormCollection.Setup(fc => fc.Files).Returns(new FormFileCollection());

        // Act
        var results = await _fileService.BatchUploadAsync(mockFormCollection.Object);

        // Assert
        Assert.Empty(results);
    }

    #endregion

    #region GenerateShareLink Tests

    [Fact]
    public async Task GenerateShareLink_WithValidFileId_ReturnsValidShareLink()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync();

        // Act
        var shareLink = _fileService.GenerateShareLink(uploadedFile.Id, 24);

        // Assert
        Assert.NotNull(shareLink);
        Assert.Contains("/api/files/download/", shareLink);
        Assert.StartsWith("https://api.securevault.com", shareLink);
    }

    [Fact]
    public async Task GenerateShareLink_CalculatesExpirationDateCorrectly()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync();
        var validHours = 48.0;
        var beforeGenerateCall = DateTime.UtcNow;

        // Act
        _fileService.GenerateShareLink(uploadedFile.Id, validHours);

        var afterGenerateCall = DateTime.UtcNow;

        // Assert - Verify expiration date is correctly set relative to UtcNow
        // The expiration should be between [beforeCall + validHours] and [afterCall + validHours]
        var expectedMin = beforeGenerateCall.AddHours(validHours);
        var expectedMax = afterGenerateCall.AddHours(validHours);

        Assert.NotNull(uploadedFile.ExpirationDate);
        Assert.InRange(uploadedFile.ExpirationDate.Value, expectedMin, expectedMax);
    }

    [Fact]
    public async Task GenerateShareLink_WithValidHoursZero_ThrowsArgumentException()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            _fileService.GenerateShareLink(uploadedFile.Id, 0));
        Assert.Contains("Valid hours must be greater than 0", exception.Message);
    }

    [Fact]
    public async Task GenerateShareLink_WithNegativeValidHours_ThrowsArgumentException()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => 
            _fileService.GenerateShareLink(uploadedFile.Id, -5));
        Assert.Contains("Valid hours must be greater than 0", exception.Message);
    }

    [Fact]
    public async Task GenerateShareLink_WithExcessiveValidHours_ThrowsArgumentException()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync();

        // Act & Assert - validHours > 8760 (1 year)
        var exception = Assert.Throws<ArgumentException>(() => 
            _fileService.GenerateShareLink(uploadedFile.Id, 9000));
        Assert.Contains("cannot exceed 8760 hours", exception.Message);
    }

    [Fact]
    public void GenerateShareLink_WithNonExistentFileId_ThrowsKeyNotFoundException()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act & Assert
        var exception = Assert.Throws<KeyNotFoundException>(() => 
            _fileService.GenerateShareLink(nonExistentId, 24));
        Assert.Contains($"File with ID {nonExistentId} not found", exception.Message);
    }

    [Fact]
    public async Task GenerateShareLink_GeneratesUniqueTokensForDifferentFiles()
    {
        // Arrange
        var file1 = await UploadTestFileAsync("file1.txt");
        var file2 = await UploadTestFileAsync("file2.txt");

        // Act
        var link1 = _fileService.GenerateShareLink(file1.Id, 24);
        var token1 = link1.Split('/').Last();

        var link2 = _fileService.GenerateShareLink(file2.Id, 48);
        var token2 = link2.Split('/').Last();

        // Assert - Tokens should be different for different files
        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public async Task GenerateShareLink_SetsShareTokenInStoredFile()
    {
        // Arrange
        var uploadedFile = await UploadTestFileAsync();

        // Act
        var shareLink = _fileService.GenerateShareLink(uploadedFile.Id, 24);

        // Assert - Verify ShareToken is populated
        Assert.NotEmpty(uploadedFile.ShareToken);
        Assert.Equal(32, uploadedFile.ShareToken.Length); // Token length is 32
        Assert.Contains(uploadedFile.ShareToken, shareLink);
    }

    #endregion
}
