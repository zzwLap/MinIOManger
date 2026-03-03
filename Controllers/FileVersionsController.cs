using Microsoft.AspNetCore.Mvc;
using MinIOStorageService.Data.Entities;
using MinIOStorageService.Services;

namespace MinIOStorageService.Controllers;

[ApiController]
[Route("api/filecache/{fileId}/versions")]
public class FileVersionsController : ControllerBase
{
    private readonly IFileVersionService _versionService;
    private readonly ILogger<FileVersionsController> _logger;

    public FileVersionsController(IFileVersionService versionService, ILogger<FileVersionsController> logger)
    {
        _versionService = versionService;
        _logger = logger;
    }

    /// <summary>
    /// 获取文件的所有版本列表
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetVersions(Guid fileId, [FromQuery] bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var versions = await _versionService.GetVersionsAsync(fileId, includeDeleted, cancellationToken);
        return Ok(new
        {
            fileId,
            count = versions.Count,
            versions = versions.Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.FileHash,
                v.Size,
                v.CreatedAt,
                v.CreatedBy,
                v.ChangeDescription,
                v.IsLatest,
                v.IsDeleted,
                v.DeletedAt
            })
        });
    }

    /// <summary>
    /// 获取指定版本信息
    /// </summary>
    [HttpGet("{versionId}")]
    public async Task<IActionResult> GetVersion(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _versionService.GetVersionAsync(versionId, cancellationToken);
        if (version == null)
        {
            return NotFound(new { message = "版本不存在", versionId });
        }

        return Ok(version);
    }

    /// <summary>
    /// 下载指定版本文件
    /// </summary>
    [HttpGet("{versionId}/download")]
    public async Task<IActionResult> DownloadVersion(Guid versionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var (stream, version) = await _versionService.DownloadVersionAsync(versionId, cancellationToken);
            return File(stream, "application/octet-stream", $"v{version.VersionNumber}_{version.FileRecord?.FileName ?? "file"}");
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "版本不存在", versionId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 恢复到指定版本
    /// </summary>
    [HttpPost("{versionId}/restore")]
    public async Task<IActionResult> RestoreVersion(Guid versionId, [FromQuery] string? description = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var newVersion = await _versionService.RestoreVersionAsync(versionId, description, cancellationToken);
            if (newVersion == null)
            {
                return NotFound(new { message = "版本不存在或已删除", versionId });
            }

            return Ok(new
            {
                message = "版本恢复成功",
                originalVersionId = versionId,
                newVersion = new
                {
                    newVersion.Id,
                    newVersion.VersionNumber,
                    newVersion.CreatedAt
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复版本失败: {VersionId}", versionId);
            return StatusCode(500, new { message = "恢复版本失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 软删除指定版本
    /// </summary>
    [HttpDelete("{versionId}")]
    public async Task<IActionResult> SoftDeleteVersion(Guid versionId, CancellationToken cancellationToken = default)
    {
        var success = await _versionService.SoftDeleteVersionAsync(versionId, cancellationToken);
        if (success)
        {
            return Ok(new { message = "版本已软删除", versionId });
        }
        return NotFound(new { message = "版本不存在", versionId });
    }

    /// <summary>
    /// 恢复软删除的版本
    /// </summary>
    [HttpPost("{versionId}/undelete")]
    public async Task<IActionResult> UndeleteVersion(Guid versionId, CancellationToken cancellationToken = default)
    {
        var success = await _versionService.UndeleteVersionAsync(versionId, cancellationToken);
        if (success)
        {
            return Ok(new { message = "版本已恢复", versionId });
        }
        return NotFound(new { message = "版本不存在或未删除", versionId });
    }

    /// <summary>
    /// 彻底删除指定版本（物理删除）
    /// </summary>
    [HttpDelete("{versionId}/permanent")]
    public async Task<IActionResult> PermanentlyDeleteVersion(Guid versionId, CancellationToken cancellationToken = default)
    {
        var success = await _versionService.PermanentlyDeleteVersionAsync(versionId, cancellationToken);
        if (success)
        {
            return Ok(new { message = "版本已彻底删除", versionId });
        }
        return NotFound(new { message = "版本不存在", versionId });
    }

    /// <summary>
    /// 彻底删除文件的所有版本
    /// </summary>
    [HttpDelete("all/permanent")]
    public async Task<IActionResult> PermanentlyDeleteAllVersions(Guid fileId, CancellationToken cancellationToken = default)
    {
        var success = await _versionService.PermanentlyDeleteFileAsync(fileId, cancellationToken);
        if (success)
        {
            return Ok(new { message = "文件及所有版本已彻底删除", fileId });
        }
        return NotFound(new { message = "文件不存在", fileId });
    }

    /// <summary>
    /// 清理旧版本（保留最近N个）
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupOldVersions(Guid fileId, [FromQuery] int keepVersions = 5, CancellationToken cancellationToken = default)
    {
        if (keepVersions < 1)
        {
            return BadRequest(new { message = "保留版本数必须大于0" });
        }

        var deletedCount = await _versionService.CleanupOldVersionsAsync(fileId, keepVersions, cancellationToken);
        return Ok(new
        {
            message = "旧版本清理完成",
            fileId,
            keepVersions,
            deletedCount
        });
    }

    /// <summary>
    /// 比较两个版本
    /// </summary>
    [HttpPost("compare")]
    public async Task<IActionResult> CompareVersions([FromQuery] Guid versionId1, [FromQuery] Guid versionId2, CancellationToken cancellationToken = default)
    {
        var areEqual = await _versionService.CompareVersionsAsync(versionId1, versionId2, cancellationToken);
        return Ok(new
        {
            versionId1,
            versionId2,
            areEqual,
            message = areEqual ? "两个版本内容相同" : "两个版本内容不同"
        });
    }

    /// <summary>
    /// 获取版本统计信息
    /// </summary>
    [HttpGet("statistics")]
    public async Task<IActionResult> GetStatistics(Guid fileId, CancellationToken cancellationToken = default)
    {
        var stats = await _versionService.GetStatisticsAsync(fileId, cancellationToken);
        return Ok(new
        {
            fileId,
            stats
        });
    }
}
