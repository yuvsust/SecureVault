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
}