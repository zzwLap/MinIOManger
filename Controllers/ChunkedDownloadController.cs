using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data;
using MinIOStorageService.Data.Entities;
using MinIOStorageService.Services;
using System.ComponentModel.DataAnnotations;

namespace MinIOStorageService.Controllers;

/// <summary>
/// 分片下载控制器 - 支持断点续传（HTTP Range + 预签名URL + 服务端代理）
/// </summary>
[ApiController]
[Route("api/download")]
public class ChunkedDownloadController : ControllerBase
{
    private readonly FileDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly IChunkedDownloadService _downloadService;
    private readonly ILogger<ChunkedDownloadController> _logger;

    public ChunkedDownloadController(
        FileDbContext dbContext,
        IStorageProvider storageProvider,
        IChunkedDownloadService downloadService,
        ILogger<ChunkedDownloadController> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _downloadService = downloadService;
        _logger = logger;
    }

    /// <summary>
    /// 获取文件信息（用于断点续传初始化）
    /// </summary>
    [HttpGet("{versionId}/info")]
    public async Task<IActionResult> GetFileInfo(Guid versionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _dbContext.FileVersions
                .Include(v => v.FileRecord)
                .FirstOrDefaultAsync(v => v.Id == versionId && !v.IsDeleted, cancellationToken);

            if (version == null)
            {
                return NotFound(new { message = "文件版本不存在", versionId });
            }

            // 获取文件大小
            var fileSize = await _storageProvider.GetFileSizeAsync(version.ObjectName, cancellationToken);

            return Ok(new
            {
                versionId = version.Id,
                fileRecordId = version.FileRecordId,
                fileName = version.FileRecord?.FileName ?? "unknown",
                contentType = version.FileRecord?.ContentType ?? "application/octet-stream",
                fileSize = fileSize,
                fileHash = version.FileHash,
                acceptRanges = "bytes",
                chunkSize = 5 * 1024 * 1024 // 建议分片大小 5MB
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取文件信息失败: {VersionId}", versionId);
            return StatusCode(500, new { message = "获取文件信息失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 下载文件（支持 HTTP Range 断点续传）- 方案1
    /// </summary>
    [HttpGet("{versionId}")]
    public async Task<IActionResult> DownloadWithRange(
        Guid versionId,
        [FromHeader(Name = "Range")] string? rangeHeader,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _dbContext.FileVersions
                .Include(v => v.FileRecord)
                .FirstOrDefaultAsync(v => v.Id == versionId && !v.IsDeleted, cancellationToken);

            if (version == null)
            {
                return NotFound(new { message = "文件版本不存在", versionId });
            }

            var fileSize = await _storageProvider.GetFileSizeAsync(version.ObjectName, cancellationToken);
            var fileName = version.FileRecord?.FileName ?? $"file_{versionId}";
            var contentType = version.FileRecord?.ContentType ?? "application/octet-stream";

            // 解析 Range 头部
            var (start, end, isRangeRequest) = ParseRangeHeader(rangeHeader, fileSize);

            if (isRangeRequest)
            {
                // 范围请求 - 返回 206 Partial Content
                var stream = await _storageProvider.GetRangeStreamAsync(version.ObjectName, start, end, cancellationToken);
                var contentLength = end - start + 1;

                Response.StatusCode = 206;
                Response.Headers.Append("Accept-Ranges", "bytes");
                Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{fileSize}");
                Response.Headers.Append("Content-Length", contentLength.ToString());

                return File(stream, contentType, fileName);
            }
            else
            {
                // 完整文件下载
                var stream = await _storageProvider.GetStreamAsync(version.ObjectName, cancellationToken);
                
                Response.Headers.Append("Accept-Ranges", "bytes");
                Response.Headers.Append("Content-Length", fileSize.ToString());

                return File(stream, contentType, fileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载文件失败: {VersionId}", versionId);
            return StatusCode(500, new { message = "下载文件失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取分片下载计划（预签名URL方案）- 方案2
    /// </summary>
    [HttpGet("{versionId}/chunks")]
    public async Task<IActionResult> GetChunkDownloadPlan(
        Guid versionId,
        [FromQuery] long chunkSize = 5 * 1024 * 1024,
        [FromQuery] int expiryMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _dbContext.FileVersions
                .Include(v => v.FileRecord)
                .FirstOrDefaultAsync(v => v.Id == versionId && !v.IsDeleted, cancellationToken);

            if (version == null)
            {
                return NotFound(new { message = "文件版本不存在", versionId });
            }

            var fileSize = await _storageProvider.GetFileSizeAsync(version.ObjectName, cancellationToken);
            var totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);
            var expiration = TimeSpan.FromMinutes(expiryMinutes);

            var chunks = new List<object>();
            for (int i = 0; i < totalChunks; i++)
            {
                var start = i * chunkSize;
                var end = Math.Min(start + chunkSize - 1, fileSize - 1);
                
                // 生成预签名URL
                var url = await _storageProvider.GetPresignedDownloadUrlAsync(
                    version.ObjectName, 
                    expiration, 
                    start, 
                    end, 
                    cancellationToken);

                chunks.Add(new
                {
                    index = i,
                    start = start,
                    end = end,
                    size = end - start + 1,
                    url = url
                });
            }

            return Ok(new
            {
                versionId = version.Id,
                fileName = version.FileRecord?.FileName ?? "unknown",
                fileSize = fileSize,
                chunkSize = chunkSize,
                totalChunks = totalChunks,
                expiryMinutes = expiryMinutes,
                chunks = chunks
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取分片下载计划失败: {VersionId}", versionId);
            return StatusCode(500, new { message = "获取分片下载计划失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取单个分片的预签名URL
    /// </summary>
    [HttpGet("{versionId}/chunks/{chunkIndex}")]
    public async Task<IActionResult> GetChunkUrl(
        Guid versionId,
        int chunkIndex,
        [FromQuery] long chunkSize = 5 * 1024 * 1024,
        [FromQuery] int expiryMinutes = 60,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var version = await _dbContext.FileVersions
                .FirstOrDefaultAsync(v => v.Id == versionId && !v.IsDeleted, cancellationToken);

            if (version == null)
            {
                return NotFound(new { message = "文件版本不存在", versionId });
            }

            var fileSize = await _storageProvider.GetFileSizeAsync(version.ObjectName, cancellationToken);
            var totalChunks = (int)Math.Ceiling((double)fileSize / chunkSize);

            if (chunkIndex < 0 || chunkIndex >= totalChunks)
            {
                return BadRequest(new { message = "分片索引超出范围", chunkIndex, totalChunks });
            }

            var start = chunkIndex * chunkSize;
            var end = Math.Min(start + chunkSize - 1, fileSize - 1);
            var expiration = TimeSpan.FromMinutes(expiryMinutes);

            var url = await _storageProvider.GetPresignedDownloadUrlAsync(
                version.ObjectName, 
                expiration, 
                start, 
                end, 
                cancellationToken);

            return Ok(new
            {
                index = chunkIndex,
                start = start,
                end = end,
                size = end - start + 1,
                url = url,
                expiresAt = DateTime.UtcNow.Add(expiration)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取分片URL失败: {VersionId}, Chunk: {ChunkIndex}", versionId, chunkIndex);
            return StatusCode(500, new { message = "获取分片URL失败", error = ex.Message });
        }
    }

    #region 服务端代理流方案（方案3）

    /// <summary>
    /// 创建下载任务（服务端代理流方案）
    /// </summary>
    [HttpPost("tasks")]
    public async Task<IActionResult> CreateDownloadTask(
        [FromBody] CreateDownloadTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var clientId = request.ClientId ?? HttpContext.Connection.Id ?? Guid.NewGuid().ToString();
            
            var task = await _downloadService.CreateDownloadTaskAsync(
                request.FileVersionId, 
                clientId, 
                cancellationToken);

            return Ok(new
            {
                message = "下载任务创建成功",
                taskId = task.Id,
                fileVersionId = task.FileVersionId,
                fileSize = task.FileSize,
                status = task.Status.ToString(),
                expiresAt = task.ExpiresAt
            });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建下载任务失败");
            return StatusCode(500, new { message = "创建下载任务失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取下载任务状态（用于断点续传）
    /// </summary>
    [HttpGet("tasks/{taskId}/status")]
    public async Task<IActionResult> GetDownloadTaskStatus(Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _downloadService.GetDownloadStatusAsync(taskId, cancellationToken);

            return Ok(new
            {
                taskId = status.TaskId,
                fileVersionId = status.FileVersionId,
                fileName = status.FileName,
                fileSize = status.FileSize,
                downloadedBytes = status.DownloadedBytes,
                progressPercent = Math.Round(status.ProgressPercent, 2),
                state = status.State.ToString(),
                createdAt = status.CreatedAt,
                expiresAt = status.ExpiresAt,
                completedAt = status.CompletedAt
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "下载任务不存在", taskId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取下载任务状态失败: {TaskId}", taskId);
            return StatusCode(500, new { message = "获取下载任务状态失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取分片下载计划（服务端代理方案）
    /// </summary>
    [HttpGet("tasks/{taskId}/chunks")]
    public async Task<IActionResult> GetProxyChunkPlan(
        Guid taskId,
        [FromQuery] long chunkSize = 5 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var plan = await _downloadService.GetChunkDownloadPlanAsync(taskId, chunkSize, null, cancellationToken);

            return Ok(new
            {
                taskId = plan.TaskId,
                fileName = plan.FileName,
                fileSize = plan.FileSize,
                chunkSize = plan.ChunkSize,
                totalChunks = plan.TotalChunks,
                chunks = plan.Chunks.Select(c => new
                {
                    index = c.Index,
                    start = c.Start,
                    end = c.End,
                    size = c.Size,
                    url = c.Url  // 服务端代理URL
                })
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "下载任务不存在", taskId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取分片下载计划失败: {TaskId}", taskId);
            return StatusCode(500, new { message = "获取分片下载计划失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 下载指定分片（服务端代理流）
    /// </summary>
    [HttpGet("proxy/{taskId}/chunks/{chunkIndex}")]
    public async Task<IActionResult> DownloadProxyChunk(
        Guid taskId,
        int chunkIndex,
        [FromQuery] long chunkSize = 5 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (stream, chunk) = await ((ChunkedDownloadService)_downloadService).DownloadChunkAsync(
                taskId, chunkIndex, chunkSize, cancellationToken);

            var fileName = $"chunk_{chunkIndex}";
            
            Response.Headers.Append("X-Chunk-Index", chunkIndex.ToString());
            Response.Headers.Append("X-Chunk-Start", chunk.Start.ToString());
            Response.Headers.Append("X-Chunk-End", chunk.End.ToString());
            Response.Headers.Append("X-Chunk-Size", chunk.Size.ToString());

            return File(stream, "application/octet-stream", fileName);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "下载任务不存在", taskId });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message, taskId, chunkIndex });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message, taskId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载分片失败: {TaskId}, Chunk: {ChunkIndex}", taskId, chunkIndex);
            return StatusCode(500, new { message = "下载分片失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 更新下载进度
    /// </summary>
    [HttpPost("tasks/{taskId}/progress")]
    public async Task<IActionResult> UpdateProgress(
        Guid taskId,
        [FromBody] UpdateProgressRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _downloadService.UpdateProgressAsync(taskId, request.DownloadedBytes, cancellationToken);

            return Ok(new
            {
                message = "进度更新成功",
                taskId,
                downloadedBytes = request.DownloadedBytes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新下载进度失败: {TaskId}", taskId);
            return StatusCode(500, new { message = "更新下载进度失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 完成下载任务
    /// </summary>
    [HttpPost("tasks/{taskId}/complete")]
    public async Task<IActionResult> CompleteDownload(Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _downloadService.CompleteDownloadAsync(taskId, cancellationToken);

            return Ok(new
            {
                message = "下载任务已完成",
                taskId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "完成下载任务失败: {TaskId}", taskId);
            return StatusCode(500, new { message = "完成下载任务失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 取消下载任务
    /// </summary>
    [HttpDelete("tasks/{taskId}")]
    public async Task<IActionResult> CancelDownload(Guid taskId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _downloadService.CancelDownloadAsync(taskId, cancellationToken);

            return Ok(new
            {
                message = "下载任务已取消",
                taskId
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message, taskId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消下载任务失败: {TaskId}", taskId);
            return StatusCode(500, new { message = "取消下载任务失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 清理过期的下载任务
    /// </summary>
    [HttpPost("tasks/cleanup")]
    public async Task<IActionResult> CleanupExpiredTasks(CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _downloadService.CleanupExpiredTasksAsync(cancellationToken);

            return Ok(new
            {
                message = "过期任务清理完成",
                cleanedCount = count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期任务失败");
            return StatusCode(500, new { message = "清理过期任务失败", error = ex.Message });
        }
    }

    #endregion

    #region 私有辅助方法

    private (long Start, long End, bool IsRangeRequest) ParseRangeHeader(string? rangeHeader, long fileSize)
    {
        if (string.IsNullOrEmpty(rangeHeader))
        {
            return (0, fileSize - 1, false);
        }

        // 解析 Range: bytes=start-end 格式
        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return (0, fileSize - 1, false);
        }

        var range = rangeHeader.Substring(6).Trim();
        var parts = range.Split('-');

        if (parts.Length != 2)
        {
            return (0, fileSize - 1, false);
        }

        long start = 0;
        long end = fileSize - 1;

        if (!string.IsNullOrEmpty(parts[0]) && long.TryParse(parts[0], out var parsedStart))
        {
            start = parsedStart;
        }

        if (!string.IsNullOrEmpty(parts[1]) && long.TryParse(parts[1], out var parsedEnd))
        {
            end = parsedEnd;
        }

        // 验证范围
        if (start < 0) start = 0;
        if (end >= fileSize) end = fileSize - 1;
        if (start > end) start = end;

        return (start, end, true);
    }

    #endregion
}

#region 请求模型

public class CreateDownloadTaskRequest
{
    [Required]
    public Guid FileVersionId { get; set; }
    
    /// <summary>
    /// 客户端标识（可选，默认使用连接ID）
    /// </summary>
    public string? ClientId { get; set; }
}

public class UpdateProgressRequest
{
    [Required]
    public long DownloadedBytes { get; set; }
}

#endregion
