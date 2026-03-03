using Microsoft.AspNetCore.Mvc;
using MinIOStorageService.Data.Entities;
using MinIOStorageService.Services;

namespace MinIOStorageService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FileCacheController : ControllerBase
{
    private readonly IFileCacheService _fileCacheService;
    private readonly ILogger<FileCacheController> _logger;

    public FileCacheController(IFileCacheService fileCacheService, ILogger<FileCacheController> logger)
    {
        _fileCacheService = fileCacheService;
        _logger = logger;
    }

    /// <summary>
    /// 上传文件并记录到数据库（支持文件夹路径）
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery] string? folder = null,
        [FromQuery] string? description = null,
        [FromQuery] string? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择要上传的文件" });
        }

        try
        {
            var record = await _fileCacheService.UploadAndRecordAsync(file, folder, description, tags, cancellationToken);
            return Ok(new
            {
                message = "文件上传成功",
                fileId = record.Id,
                fileName = record.FileName,
                objectName = record.ObjectName,
                size = record.Size,
                fileHash = record.FileHash,
                createdAt = record.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件上传失败");
            return StatusCode(500, new { message = "文件上传失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 下载单个文件（自动使用本地缓存或从MinIO下载）
    /// </summary>
    [HttpGet("download/{fileId}")]
    public async Task<IActionResult> DownloadFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (stream, record) = await _fileCacheService.GetFileAsync(fileId, cancellationToken);
            return File(stream, record.ContentType, record.FileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "文件不存在", fileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件下载失败: {FileId}", fileId);
            return StatusCode(500, new { message = "文件下载失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 批量下载文件（打包为ZIP）
    /// </summary>
    [HttpPost("batch-download")]
    public async Task<IActionResult> BatchDownload([FromBody] List<Guid> fileIds, CancellationToken cancellationToken = default)
    {
        if (fileIds == null || fileIds.Count == 0)
        {
            return BadRequest(new { message = "请选择要下载的文件" });
        }

        try
        {
            var (stream, fileName) = await _fileCacheService.BatchDownloadAsync(fileIds, cancellationToken);
            return File(stream, "application/zip", fileName);
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量下载失败");
            return StatusCode(500, new { message = "批量下载失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取文件记录列表
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> GetFileRecords(
        [FromQuery] string? search = null,
        [FromQuery] string? tags = null,
        CancellationToken cancellationToken = default)
    {
        var records = await _fileCacheService.GetFileRecordsAsync(search, tags, cancellationToken);
        return Ok(new
        {
            count = records.Count,
            files = records.Select(r => new
            {
                r.Id,
                r.FileName,
                r.ObjectName,
                r.Size,
                r.ContentType,
                r.FileHash,
                r.IsCachedLocally,
                r.LastSyncedAt,
                r.CreatedAt,
                r.Description,
                r.Tags
            })
        });
    }

    /// <summary>
    /// 获取单个文件记录详情
    /// </summary>
    [HttpGet("{fileId}")]
    public async Task<IActionResult> GetFileRecord(Guid fileId, CancellationToken cancellationToken = default)
    {
        var records = await _fileCacheService.GetFileRecordsAsync(cancellationToken: cancellationToken);
        var record = records.FirstOrDefault(r => r.Id == fileId);
        
        if (record == null)
        {
            return NotFound(new { message = "文件记录不存在", fileId });
        }

        return Ok(record);
    }

    /// <summary>
    /// 同步文件（检查本地缓存与远程一致性）
    /// </summary>
    [HttpPost("sync/{fileId}")]
    public async Task<IActionResult> SyncFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _fileCacheService.SyncFileAsync(fileId, cancellationToken);
            if (success)
            {
                return Ok(new { message = "文件同步成功", fileId });
            }
            return BadRequest(new { message = "文件同步失败", fileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件同步失败: {FileId}", fileId);
            return StatusCode(500, new { message = "文件同步失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 强制刷新本地缓存
    /// </summary>
    [HttpPost("refresh/{fileId}")]
    public async Task<IActionResult> RefreshCache(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _fileCacheService.RefreshCacheAsync(fileId, cancellationToken);
            if (success)
            {
                return Ok(new { message = "缓存刷新成功", fileId });
            }
            return NotFound(new { message = "文件不存在", fileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "缓存刷新失败: {FileId}", fileId);
            return StatusCode(500, new { message = "缓存刷新失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 删除文件（数据库+MinIO+本地缓存）
    /// </summary>
    [HttpDelete("{fileId}")]
    public async Task<IActionResult> DeleteFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _fileCacheService.DeleteFileAsync(fileId, cancellationToken);
            if (success)
            {
                return Ok(new { message = "文件删除成功", fileId });
            }
            return NotFound(new { message = "文件不存在", fileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件删除失败: {FileId}", fileId);
            return StatusCode(500, new { message = "文件删除失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 清理过期缓存
    /// </summary>
    [HttpPost("clean-cache")]
    public async Task<IActionResult> CleanExpiredCache([FromQuery] int maxAgeHours = 168, CancellationToken cancellationToken = default)
    {
        try
        {
            var maxAge = TimeSpan.FromHours(maxAgeHours);
            var cleanedCount = await _fileCacheService.CleanExpiredCacheAsync(maxAge, cancellationToken);
            return Ok(new 
            { 
                message = "过期缓存清理完成", 
                cleanedCount,
                maxAgeHours 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期缓存失败");
            return StatusCode(500, new { message = "清理过期缓存失败", error = ex.Message });
        }
    }
}
