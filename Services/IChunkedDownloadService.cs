using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Services;

/// <summary>
/// 分片下载服务接口 - 支持断点续传下载
/// </summary>
public interface IChunkedDownloadService
{
    /// <summary>
    /// 创建下载任务
    /// </summary>
    /// <param name="fileVersionId">文件版本ID</param>
    /// <param name="clientId">客户端标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下载任务信息</returns>
    Task<DownloadTask> CreateDownloadTaskAsync(Guid fileVersionId, string clientId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取下载任务状态
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>下载任务状态</returns>
    Task<DownloadTaskInfo> GetDownloadStatusAsync(Guid taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 下载指定范围的数据（HTTP Range方案）
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="start">起始位置</param>
    /// <param name="end">结束位置（null表示到文件末尾）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>数据流和元数据</returns>
    Task<(Stream Stream, long TotalSize, long Start, long End)> DownloadRangeAsync(
        Guid taskId, 
        long start, 
        long? end = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取分片下载URL列表（预签名URL方案）
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="chunkSize">分片大小</param>
    /// <param name="expiration">URL过期时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>分片URL列表</returns>
    Task<ChunkDownloadPlan> GetChunkDownloadPlanAsync(
        Guid taskId, 
        long chunkSize = 5 * 1024 * 1024, 
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 更新下载进度
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="downloadedBytes">已下载字节数</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UpdateProgressAsync(Guid taskId, long downloadedBytes, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 完成下载任务
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task CompleteDownloadAsync(Guid taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 取消下载任务
    /// </summary>
    /// <param name="taskId">任务ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task CancelDownloadAsync(Guid taskId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清理过期的下载任务
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>清理的任务数量</returns>
    Task<int> CleanupExpiredTasksAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 下载任务信息
/// </summary>
public class DownloadTaskInfo
{
    public Guid TaskId { get; set; }
    public Guid FileVersionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long DownloadedBytes { get; set; }
    public double ProgressPercent => FileSize > 0 ? (DownloadedBytes * 100.0 / FileSize) : 0;
    public DownloadTaskState State { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// 分片下载计划
/// </summary>
public class ChunkDownloadPlan
{
    public Guid TaskId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long ChunkSize { get; set; }
    public int TotalChunks { get; set; }
    public List<ChunkInfo> Chunks { get; set; } = new();
}

/// <summary>
/// 分片信息
/// </summary>
public class ChunkInfo
{
    public int Index { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public long Size => End - Start + 1;
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// 下载任务状态枚举
/// </summary>
public enum DownloadTaskState
{
    Pending = 0,
    Downloading = 1,
    Paused = 2,
    Completed = 3,
    Cancelled = 4,
    Expired = 5,
    Failed = 6
}
