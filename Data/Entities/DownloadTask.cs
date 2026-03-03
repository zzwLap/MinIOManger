namespace MinIOStorageService.Data.Entities;

/// <summary>
/// 下载任务 - 用于断点续传下载
/// </summary>
public class DownloadTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// 文件版本ID
    /// </summary>
    public Guid FileVersionId { get; set; }
    
    /// <summary>
    /// 客户端标识（设备ID或会话ID）
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
    
    /// <summary>
    /// 文件总大小
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// 已下载字节数
    /// </summary>
    public long DownloadedBytes { get; set; }
    
    /// <summary>
    /// 下载状态
    /// </summary>
    public DownloadTaskStatus Status { get; set; } = DownloadTaskStatus.Pending;
    
    /// <summary>
    /// 已下载的分片序号（JSON数组）
    /// </summary>
    public string DownloadedChunks { get; set; } = "[]";
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 过期时间（默认24小时）
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    
    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// 最后更新时间
    /// </summary>
    public DateTime? LastUpdatedAt { get; set; }
    
    /// <summary>
    /// 错误信息（如果失败）
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    // 导航属性
    public FileVersion FileVersion { get; set; } = null!;
}

/// <summary>
/// 下载任务状态
/// </summary>
public enum DownloadTaskStatus
{
    /// <summary>
    /// 等待下载
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// 下载中
    /// </summary>
    Downloading = 1,
    
    /// <summary>
    /// 已暂停
    /// </summary>
    Paused = 2,
    
    /// <summary>
    /// 已完成
    /// </summary>
    Completed = 3,
    
    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 4,
    
    /// <summary>
    /// 已过期
    /// </summary>
    Expired = 5,
    
    /// <summary>
    /// 失败
    /// </summary>
    Failed = 6
}
