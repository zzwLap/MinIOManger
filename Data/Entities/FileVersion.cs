namespace MinIOStorageService.Data.Entities;

/// <summary>
/// 文件版本记录 - 支持历史版本管理
/// </summary>
public class FileVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// 所属文件记录ID
    /// </summary>
    public Guid FileRecordId { get; set; }
    
    /// <summary>
    /// 版本号（从1开始递增）
    /// </summary>
    public int VersionNumber { get; set; }
    
    /// <summary>
    /// 存储对象名称（包含版本标识）
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;
    
    /// <summary>
    /// 文件哈希
    /// </summary>
    public string FileHash { get; set; } = string.Empty;
    
    /// <summary>
    /// 文件大小
    /// </summary>
    public long Size { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 创建人
    /// </summary>
    public string? CreatedBy { get; set; }
    
    /// <summary>
    /// 变更说明
    /// </summary>
    public string? ChangeDescription { get; set; }
    
    /// <summary>
    /// 是否软删除
    /// </summary>
    public bool IsDeleted { get; set; }
    
    /// <summary>
    /// 删除时间
    /// </summary>
    public DateTime? DeletedAt { get; set; }
    
    /// <summary>
    /// 是否是最新版本
    /// </summary>
    public bool IsLatest { get; set; }
    
    /// <summary>
    /// 本地缓存路径
    /// </summary>
    public string? LocalCachePath { get; set; }
    
    /// <summary>
    /// 是否已缓存到本地
    /// </summary>
    public bool IsCachedLocally { get; set; }
    
    // 导航属性
    public FileRecord FileRecord { get; set; } = null!;
}
