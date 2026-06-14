using Microsoft.AspNetCore.Mvc;
using SecureVault.Api.Services;
using System.Security;

namespace SecureVault.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IFileService _fileService;

    public FilesController(IFileService fileService)
    {
        _fileService = fileService;
    }

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest("File is required and cannot be empty.");

            var storedFile = await _fileService.UploadFileAsync(file);
            return CreatedAtAction(nameof(Upload), new { id = storedFile.Id }, storedFile);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (SecurityException ex)
        {
            return Forbid(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, $"An error occurred: {ex.Message}");
        }
    }

    [HttpPost("batch-upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> BatchUpload([FromForm] List<IFormFile> files, CancellationToken ct)
    {
        try
        {
            if (!Request.HasFormContentType || files == null || files.Count == 0)
                return BadRequest("No files provided. Use multipart/form-data with at least one file.");

            // Enforce quick limits at controller level to avoid unnecessary processing
            const int MAX_FILES = 20;
            if (files.Count > MAX_FILES)
                return BadRequest($"Too many files. Maximum is {MAX_FILES}.");

            var results = await _fileService.BatchUploadAsync(files, ct);

            // Return 200 with per-file statuses; clients should inspect results for failures.
            return Ok(results);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, "Request was cancelled.");
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
        }
    }

    [HttpGet("download/{token}")]
    public async Task<IActionResult> Download(string token, CancellationToken ct)
    {
        try
        {
            var (stream, fileName) = await _fileService.DownloadFileAsync(token, ct);
            return File(stream, "application/octet-stream", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, $"An error occurred: {ex.Message}");
        }
    }

    [HttpGet("details/{id}")]
    public async Task<IActionResult> GetDetails(Guid id)
    {
        try
        {
            var details = await _fileService.GetFileDetailsAsync(id);
            return Ok(details);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, $"An error occurred: {ex.Message}");
        }
    }

    [HttpPost("{id}/extend-share")]
    public async Task<IActionResult> ExtendShare(Guid id, [FromQuery] double additionalHours, [FromHeader(Name = "X-Share-Token")] string shareToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(shareToken))
                return BadRequest("Missing X-Share-Token header.");

            var result = await _fileService.ExtendShareLinkAsync(id, additionalHours, shareToken);

            if (!result.Success)
                return BadRequest(result.Message);

            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound("File not found.");
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, "An internal error occurred.");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        try
        {
            var deleted = await _fileService.DeleteFileAsync(id);
            if (!deleted)
                return NotFound();

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, $"An error occurred: {ex.Message}");
        }
    }
}