using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Services;

/// <summary>
/// 分片上传服务接口
/// </summary>
public interface IChunkedUploadService
{
    /// <summary>
    /// 初始化上传会话
    /// </summary>
    /// <param name="fileName">文件名</param>
    /// <param name="fileSize">文件总大小</param>
    /// <param name="fileHash">文件哈希（用于秒传）</param>
    /// <param name="contentType">内容类型</param>
    /// <param name="folder">文件夹路径</param>
    /// <param name="description">文件描述</param>
    /// <param name="tags">标签</param>
    /// <param name="chunkSize">分片大小（默认5MB）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>上传会话信息</returns>
    Task<UploadSession> InitiateUploadAsync(
        string fileName,
        long fileSize,
        string? fileHash = null,
        string? contentType = null,
        string? folder = null,
        string? description = null,
        string? tags = null,
        int? chunkSize = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 上传分片
    /// </summary>
    /// <param name="uploadId">上传会话ID</param>
    /// <param name="chunkNumber">分片序号（从0开始）</param>
    /// <param name="chunkData">分片数据流</param>
    /// <param name="chunkHash">分片MD5（可选，用于校验）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分片ETag</returns>
    Task<string> UploadChunkAsync(
        Guid uploadId,
        int chunkNumber,
        Stream chunkData,
        string? chunkHash = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取上传状态（用于断点续传）
    /// </summary>
    /// <param name="uploadId">上传会话ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>上传状态和已上传分片列表</returns>
    Task<UploadStatusInfo> GetUploadStatusAsync(Guid uploadId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 完成上传并合并分片
    /// </summary>
    /// <param name="uploadId">上传会话ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的文件版本</returns>
    Task<FileVersion> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 取消上传并清理临时文件
    /// </summary>
    /// <param name="uploadId">上传会话ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功取消</returns>
    Task<bool> CancelUploadAsync(Guid uploadId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清理过期的上传会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的会话数量</returns>
    Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 尝试秒传（如果文件已存在且哈希匹配）
    /// </summary>
    /// <param name="fileHash">文件哈希</param>
    /// <param name="fileName">文件名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>秒传结果，成功返回文件版本</returns>
    Task<QuickUploadResult?> TryQuickUploadAsync(string fileHash, string fileName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 上传状态信息
/// </summary>
public class UploadStatusInfo
{
    public Guid UploadId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int TotalChunks { get; set; }
    public int UploadedChunkCount { get; set; }
    public List<int> UploadedChunks { get; set; } = new();
    public List<int> MissingChunks { get; set; } = new();
    public UploadStatus Status { get; set; }
    public double ProgressPercent => TotalChunks > 0 ? (UploadedChunkCount * 100.0 / TotalChunks) : 0;
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// 秒传结果
/// </summary>
public class QuickUploadResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public FileVersion? FileVersion { get; set; }
    public Guid? FileRecordId { get; set; }
}
