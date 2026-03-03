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
    /// 上传文件并创建新版本（支持文件夹路径）
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadFile(
        IFormFile file,
        [FromQuery] string? folder = null,
        [FromQuery] string? description = null,
        [FromQuery] string? tags = null,
        [FromQuery] string? changeDescription = null,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "请选择要上传的文件" });
        }

        try
        {
            var version = await _fileCacheService.UploadAndRecordAsync(file, folder, description, tags, changeDescription, cancellationToken);
            return Ok(new
            {
                message = "文件上传成功",
                fileId = version.FileRecordId,
                versionId = version.Id,
                versionNumber = version.VersionNumber,
                fileName = version.FileRecord?.FileName ?? file.FileName,
                size = version.Size,
                fileHash = version.FileHash,
                createdAt = version.CreatedAt,
                isLatest = version.IsLatest
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件上传失败");
            return StatusCode(500, new { message = "文件上传失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 下载文件最新版本
    /// </summary>
    [HttpGet("download/{fileId}")]
    public async Task<IActionResult> DownloadFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (stream, version) = await _fileCacheService.GetFileAsync(fileId, cancellationToken);
            var fileName = version.FileRecord?.FileName ?? $"file_{fileId}";
            return File(stream, "application/octet-stream", fileName);
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
    /// 获取文件列表
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> GetFileRecords(
        [FromQuery] string? search = null,
        [FromQuery] string? tags = null,
        [FromQuery] bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        var records = await _fileCacheService.GetFileRecordsAsync(search, tags, includeDeleted, cancellationToken);
        return Ok(new
        {
            count = records.Count,
            files = records.Select(r => new
            {
                r.Id,
                r.FileName,
                r.ContentType,
                r.VersionCount,
                r.CreatedAt,
                r.UpdatedAt,
                r.Description,
                r.Tags,
                r.IsDeleted,
                currentVersion = r.CurrentVersion == null ? null : new
                {
                    r.CurrentVersion.Id,
                    r.CurrentVersion.VersionNumber,
                    r.CurrentVersion.Size,
                    r.CurrentVersion.FileHash,
                    r.CurrentVersion.CreatedAt
                }
            })
        });
    }

    /// <summary>
    /// 获取文件详情（含版本列表）
    /// </summary>
    [HttpGet("{fileId}")]
    public async Task<IActionResult> GetFileRecord(Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await _fileCacheService.GetFileRecordAsync(fileId, cancellationToken);
        
        if (record == null)
        {
            return NotFound(new { message = "文件记录不存在", fileId });
        }

        return Ok(new
        {
            record.Id,
            record.FileName,
            record.ContentType,
            record.Description,
            record.Tags,
            record.VersionCount,
            record.CreatedAt,
            record.UpdatedAt,
            record.IsDeleted,
            record.DeletedAt,
            currentVersion = record.CurrentVersion == null ? null : new
            {
                record.CurrentVersion.Id,
                record.CurrentVersion.VersionNumber,
                record.CurrentVersion.Size,
                record.CurrentVersion.FileHash,
                record.CurrentVersion.CreatedAt,
                record.CurrentVersion.ChangeDescription
            },
            versions = record.Versions.Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.Size,
                v.FileHash,
                v.CreatedAt,
                v.ChangeDescription,
                v.IsLatest,
                v.IsDeleted,
                v.IsCachedLocally
            })
        });
    }

    /// <summary>
    /// 同步文件最新版本（检查本地缓存与远程一致性）
    /// </summary>
    [HttpPost("sync/{fileId}")]
    public async Task<IActionResult> SyncFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _fileCacheService.GetFileRecordAsync(fileId, cancellationToken);
            if (record?.CurrentVersion == null)
            {
                return NotFound(new { message = "文件或版本不存在", fileId });
            }

            var success = await _fileCacheService.SyncFileAsync(record.CurrentVersion.Id, cancellationToken);
            if (success)
            {
                return Ok(new { message = "文件同步成功", fileId, versionId = record.CurrentVersion.Id });
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
            var record = await _fileCacheService.GetFileRecordAsync(fileId, cancellationToken);
            if (record?.CurrentVersion == null)
            {
                return NotFound(new { message = "文件或版本不存在", fileId });
            }

            var success = await _fileCacheService.RefreshCacheAsync(record.CurrentVersion.Id, cancellationToken);
            if (success)
            {
                return Ok(new { message = "缓存刷新成功", fileId, versionId = record.CurrentVersion.Id });
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
    /// 软删除文件（可恢复）
    /// </summary>
    [HttpDelete("{fileId}")]
    public async Task<IActionResult> SoftDeleteFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _fileCacheService.SoftDeleteFileAsync(fileId, cancellationToken);
            if (success)
            {
                return Ok(new { message = "文件已软删除", fileId });
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
    /// 恢复软删除的文件
    /// </summary>
    [HttpPost("{fileId}/undelete")]
    public async Task<IActionResult> UndeleteFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _fileCacheService.UndeleteFileAsync(fileId, cancellationToken);
            if (success)
            {
                return Ok(new { message = "文件已恢复", fileId });
            }
            return NotFound(new { message = "文件不存在或未删除", fileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件恢复失败: {FileId}", fileId);
            return StatusCode(500, new { message = "文件恢复失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 彻底删除文件及所有版本
    /// </summary>
    [HttpDelete("{fileId}/permanent")]
    public async Task<IActionResult> PermanentlyDeleteFile(Guid fileId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _fileCacheService.PermanentlyDeleteFileAsync(fileId, cancellationToken);
            if (success)
            {
                return Ok(new { message = "文件及所有版本已彻底删除", fileId });
            }
            return NotFound(new { message = "文件不存在", fileId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "彻底删除文件失败: {FileId}", fileId);
            return StatusCode(500, new { message = "彻底删除文件失败", error = ex.Message });
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
