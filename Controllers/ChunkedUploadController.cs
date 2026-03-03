using Microsoft.AspNetCore.Mvc;
using MinIOStorageService.Data.Entities;
using MinIOStorageService.Services;
using System.ComponentModel.DataAnnotations;

namespace MinIOStorageService.Controllers;

/// <summary>
/// 分片上传控制器 - 支持大文件断点续传
/// </summary>
[ApiController]
[Route("api/upload")]
public class ChunkedUploadController : ControllerBase
{
    private readonly IChunkedUploadService _chunkedUploadService;
    private readonly ILogger<ChunkedUploadController> _logger;

    public ChunkedUploadController(IChunkedUploadService chunkedUploadService, ILogger<ChunkedUploadController> logger)
    {
        _chunkedUploadService = chunkedUploadService;
        _logger = logger;
    }

    /// <summary>
    /// 初始化上传会话（支持秒传）
    /// </summary>
    [HttpPost("init")]
    public async Task<IActionResult> InitiateUpload([FromBody] InitiateUploadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _chunkedUploadService.InitiateUploadAsync(
                request.FileName,
                request.FileSize,
                request.FileHash,
                request.ContentType,
                request.Folder,
                request.Description,
                request.Tags,
                request.ChunkSize,
                cancellationToken);

            // 检查是否是秒传
            if (session.Status == UploadStatus.Completed)
            {
                return Ok(new
                {
                    message = "秒传成功",
                    uploadId = session.Id,
                    status = session.Status.ToString(),
                    fileRecordId = session.FileRecordId,
                    fileVersionId = session.FileVersionId,
                    isQuickUpload = true
                });
            }

            return Ok(new
            {
                message = "上传会话创建成功",
                uploadId = session.Id,
                fileName = session.FileName,
                fileSize = session.FileSize,
                chunkSize = session.ChunkSize,
                totalChunks = session.TotalChunks,
                status = session.Status.ToString(),
                expiresAt = session.ExpiresAt,
                isQuickUpload = false
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建上传会话失败");
            return StatusCode(500, new { message = "创建上传会话失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 尝试秒传
    /// </summary>
    [HttpPost("quick")]
    public async Task<IActionResult> QuickUpload([FromBody] QuickUploadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _chunkedUploadService.TryQuickUploadAsync(request.FileHash, request.FileName, cancellationToken);
            
            if (result?.Success == true)
            {
                return Ok(new
                {
                    success = true,
                    message = "秒传成功",
                    fileRecordId = result.FileRecordId,
                    fileVersionId = result.FileVersion?.Id,
                    fileName = result.FileVersion?.FileRecord?.FileName
                });
            }

            return Ok(new
            {
                success = false,
                message = "文件不存在，需要正常上传"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "秒传检查失败");
            return StatusCode(500, new { message = "秒传检查失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 上传分片
    /// </summary>
    [HttpPut("{uploadId}/chunks/{chunkNumber}")]
    public async Task<IActionResult> UploadChunk(
        Guid uploadId,
        int chunkNumber,
        IFormFile chunk,
        [FromHeader(Name = "X-Chunk-Hash")] string? chunkHash = null,
        CancellationToken cancellationToken = default)
    {
        if (chunk == null || chunk.Length == 0)
        {
            return BadRequest(new { message = "分片数据不能为空" });
        }

        try
        {
            using var stream = chunk.OpenReadStream();
            var etag = await _chunkedUploadService.UploadChunkAsync(uploadId, chunkNumber, stream, chunkHash, cancellationToken);

            return Ok(new
            {
                message = "分片上传成功",
                uploadId,
                chunkNumber,
                etag,
                size = chunk.Length
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "上传会话不存在", uploadId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "分片上传失败: {UploadId}, Chunk: {ChunkNumber}", uploadId, chunkNumber);
            return StatusCode(500, new { message = "分片上传失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取上传状态（用于断点续传）
    /// </summary>
    [HttpGet("{uploadId}/status")]
    public async Task<IActionResult> GetUploadStatus(Guid uploadId, CancellationToken cancellationToken = default)
    {
        try
        {
            var status = await _chunkedUploadService.GetUploadStatusAsync(uploadId, cancellationToken);

            return Ok(new
            {
                uploadId = status.UploadId,
                fileName = status.FileName,
                fileSize = status.FileSize,
                totalChunks = status.TotalChunks,
                uploadedChunkCount = status.UploadedChunkCount,
                uploadedChunks = status.UploadedChunks,
                missingChunks = status.MissingChunks,
                progressPercent = Math.Round(status.ProgressPercent, 2),
                status = status.Status.ToString(),
                expiresAt = status.ExpiresAt
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "上传会话不存在", uploadId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取上传状态失败: {UploadId}", uploadId);
            return StatusCode(500, new { message = "获取上传状态失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 完成上传并合并分片
    /// </summary>
    [HttpPost("{uploadId}/complete")]
    public async Task<IActionResult> CompleteUpload(Guid uploadId, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileVersion = await _chunkedUploadService.CompleteUploadAsync(uploadId, cancellationToken);

            return Ok(new
            {
                message = "上传完成",
                uploadId,
                fileRecordId = fileVersion.FileRecordId,
                fileVersionId = fileVersion.Id,
                versionNumber = fileVersion.VersionNumber,
                fileName = fileVersion.FileRecord?.FileName,
                size = fileVersion.Size,
                fileHash = fileVersion.FileHash,
                createdAt = fileVersion.CreatedAt
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "上传会话不存在", uploadId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "完成上传失败: {UploadId}", uploadId);
            return StatusCode(500, new { message = "完成上传失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 取消上传
    /// </summary>
    [HttpDelete("{uploadId}")]
    public async Task<IActionResult> CancelUpload(Guid uploadId, CancellationToken cancellationToken = default)
    {
        try
        {
            var success = await _chunkedUploadService.CancelUploadAsync(uploadId, cancellationToken);
            if (success)
            {
                return Ok(new { message = "上传已取消", uploadId });
            }
            return NotFound(new { message = "上传会话不存在", uploadId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消上传失败: {UploadId}", uploadId);
            return StatusCode(500, new { message = "取消上传失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 清理过期的上传会话
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupExpiredSessions(CancellationToken cancellationToken = default)
    {
        try
        {
            var count = await _chunkedUploadService.CleanupExpiredSessionsAsync(cancellationToken);
            return Ok(new 
            { 
                message = "过期会话清理完成", 
                cleanedCount = count 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期会话失败");
            return StatusCode(500, new { message = "清理过期会话失败", error = ex.Message });
        }
    }
}

#region 请求模型

public class InitiateUploadRequest
{
    [Required]
    public string FileName { get; set; } = string.Empty;
    
    [Required]
    public long FileSize { get; set; }
    
    /// <summary>
    /// 文件哈希（SHA256），用于秒传
    /// </summary>
    public string? FileHash { get; set; }
    
    public string? ContentType { get; set; }
    
    /// <summary>
    /// 文件夹路径
    /// </summary>
    public string? Folder { get; set; }
    
    public string? Description { get; set; }
    
    public string? Tags { get; set; }
    
    /// <summary>
    /// 分片大小（字节），默认5MB
    /// </summary>
    public int? ChunkSize { get; set; }
}

public class QuickUploadRequest
{
    [Required]
    public string FileHash { get; set; } = string.Empty;
    
    [Required]
    public string FileName { get; set; } = string.Empty;
}

#endregion
