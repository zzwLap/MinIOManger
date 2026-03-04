using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using MinIOStorageService.Models;
using MinIOStorageService.Services;
using System.Net.Mime;

namespace MinIOStorageService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FilesController : ControllerBase
{
    private readonly IMinioService _minioService;
    private readonly ILogger<FilesController> _logger;
    private readonly IContentTypeProvider _contentTypeProvider;

    public FilesController(IMinioService minioService, ILogger<FilesController> logger)
    {
        _minioService = minioService;
        _logger = logger;
        _contentTypeProvider = new FileExtensionContentTypeProvider();
    }

    /// <summary>
    /// 上传单个文件到指定文件夹
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(IFormFile file, [FromQuery] string? folder = null, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择要上传的文件" });
        }

        var objectName = await _minioService.UploadFileAsync(file, folder, cancellationToken: cancellationToken);

        return Ok(new
        {
            Success = true,
            ObjectName = objectName,
            Message = "文件上传成功"
        });
    }

    /// <summary>
    /// 上传文件到指定完整路径
    /// </summary>
    [HttpPost("upload-with-path")]
    public async Task<IActionResult> UploadFileWithPath(IFormFile file, [FromQuery] string fullPath, CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择要上传的文件" });
        }

        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return BadRequest(new { message = "请指定完整的文件路径" });
        }

        var objectName = await _minioService.UploadFileWithPathAsync(file, fullPath, cancellationToken);

        return Ok(new
        {
            Success = true,
            ObjectName = objectName,
            Message = "文件上传成功"
        });
    }

    /// <summary>
    /// 上传多个文件
    /// </summary>
    [HttpPost("upload-multiple")]
    public async Task<IActionResult> UploadMultipleFiles(List<IFormFile> files, CancellationToken cancellationToken)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(new { message = "请选择要上传的文件" });
        }

        var results = new List<object>();
        foreach (var file in files)
        {
            if (file.Length > 0)
            {
                var objectName = await _minioService.UploadFileAsync(file, cancellationToken: cancellationToken);
                results.Add(new
                {
                    Success = true,
                    ObjectName = objectName,
                    Message = "文件上传成功"
                });
            }
        }

        return Ok(new
        {
            totalCount = files.Count,
            successCount = results.Count,
            results
        });
    }

    /// <summary>
    /// 下载单个文件
    /// </summary>
    [HttpGet("download/{objectName}")]
    public async Task<IActionResult> DownloadFile(string objectName, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await _minioService.GetFileMetadataAsync(objectName, cancellationToken);
            if (metadata == null)
            {
                return NotFound(new { message = "文件不存在" });
            }

            var stream = await _minioService.DownloadFileAsync(objectName, cancellationToken);
            
            var contentType = string.IsNullOrEmpty(metadata.ContentType) 
                ? "application/octet-stream" 
                : metadata.ContentType;

            return File(stream, contentType, metadata.FileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载文件失败: {ObjectName}", objectName);
            return StatusCode(500, new { message = "下载文件失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 批量下载文件（打包为ZIP）
    /// </summary>
    [HttpPost("batch-download")]
    public async Task<IActionResult> BatchDownload([FromBody] BatchDownloadRequest request, CancellationToken cancellationToken)
    {
        if (request.ObjectNames == null || request.ObjectNames.Count == 0)
        {
            return BadRequest(new { message = "请选择要下载的文件" });
        }

        try
        {
            var zipFileName = string.IsNullOrWhiteSpace(request.ZipFileName) 
                ? $"files_{DateTime.Now:yyyyMMddHHmmss}.zip" 
                : request.ZipFileName;

            if (!zipFileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipFileName += ".zip";
            }

            var zipStream = await _minioService.CreateBatchDownloadZipAsync(
                request.ObjectNames, 
                zipFileName, 
                cancellationToken);

            return File(zipStream, MediaTypeNames.Application.Zip, zipFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量下载文件失败");
            return StatusCode(500, new { message = "批量下载失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取文件元数据
    /// </summary>
    [HttpGet("metadata/{objectName}")]
    public async Task<IActionResult> GetFileMetadata(string objectName, CancellationToken cancellationToken)
    {
        var metadata = await _minioService.GetFileMetadataAsync(objectName, cancellationToken);
        
        if (metadata == null)
        {
            return NotFound(new { message = "文件不存在" });
        }

        return Ok(metadata);
    }

    /// <summary>
    /// 获取文件列表
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> ListFiles([FromQuery] string? prefix = null, CancellationToken cancellationToken = default)
    {
        var files = await _minioService.ListFilesAsync(prefix, cancellationToken);
        return Ok(new
        {
            count = files.Count,
            files
        });
    }

    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    [HttpGet("exists/{objectName}")]
    public async Task<IActionResult> FileExists(string objectName, CancellationToken cancellationToken)
    {
        var exists = await _minioService.FileExistsAsync(objectName, cancellationToken);
        return Ok(new { objectName, exists });
    }

    /// <summary>
    /// 删除文件
    /// </summary>
    [HttpDelete("{objectName}")]
    public async Task<IActionResult> DeleteFile(string objectName, CancellationToken cancellationToken)
    {
        var success = await _minioService.DeleteFileAsync(objectName, cancellationToken);
        
        if (success)
        {
            return Ok(new { message = "文件删除成功", objectName });
        }

        return BadRequest(new { message = "文件删除失败", objectName });
    }

    /// <summary>
    /// 批量删除文件
    /// </summary>
    [HttpPost("batch-delete")]
    public async Task<IActionResult> BatchDelete([FromBody] List<string> objectNames, CancellationToken cancellationToken)
    {
        if (objectNames == null || objectNames.Count == 0)
        {
            return BadRequest(new { message = "请选择要删除的文件" });
        }

        var results = new List<object>();
        foreach (var objectName in objectNames)
        {
            var success = await _minioService.DeleteFileAsync(objectName, cancellationToken);
            results.Add(new { objectName, success });
        }

        return Ok(new
        {
            totalCount = objectNames.Count,
            successCount = results.Count(r => (bool)((dynamic)r).success),
            results
        });
    }
}
