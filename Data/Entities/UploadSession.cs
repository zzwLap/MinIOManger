namespace MinIOStorageService.Data.Entities;

/// <summary>
/// 分片上传会话
/// </summary>
public class UploadSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// 文件总大小（字节）
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// 分片大小（字节）
    /// </summary>
    public int ChunkSize { get; set; } = 5 * 1024 * 1024; // 默认5MB
    
    /// <summary>
    /// 总分片数
    /// </summary>
    public int TotalChunks { get; set; }
    
    /// <summary>
    /// 已上传的分片序号列表（JSON格式）
    /// </summary>
    public string UploadedChunks { get; set; } = "[]";
    
    /// <summary>
    /// 临时存储路径
    /// </summary>
    public string TempPath { get; set; } = string.Empty;
    
    /// <summary>
    /// 内容类型
    /// </summary>
    public string ContentType { get; set; } = string.Empty;
    
    /// <summary>
    /// 文件哈希（用于秒传）
    /// </summary>
    public string? FileHash { get; set; }
    
    /// <summary>
    /// 文件夹路径
    /// </summary>
    public string? Folder { get; set; }
    
    /// <summary>
    /// 文件描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public string? Tags { get; set; }
    
    /// <summary>
    /// 上传状态
    /// </summary>
    public UploadStatus Status { get; set; } = UploadStatus.Pending;
    
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
    /// 关联的文件记录ID（上传完成后）
    /// </summary>
    public Guid? FileRecordId { get; set; }
    
    /// <summary>
    /// 关联的文件版本ID（上传完成后）
    /// </summary>
    public Guid? FileVersionId { get; set; }
    
    // 导航属性
    public FileRecord? FileRecord { get; set; }
    public FileVersion? FileVersion { get; set; }
}

/// <summary>
/// 上传状态
/// </summary>
public enum UploadStatus
{
    /// <summary>
    /// 等待上传
    /// </summary>
    Pending = 0,
    
    /// <summary>
    /// 上传中
    /// </summary>
    Uploading = 1,
    
    /// <summary>
    /// 合并中
    /// </summary>
    Merging = 2,
    
    /// <summary>
    /// 已完成
    /// </summary>
    Completed = 3,
    
    /// <summary>
    /// 已过期
    /// </summary>
    Expired = 4,
    
    /// <summary>
    /// 已取消
    /// </summary>
    Cancelled = 5
}
