namespace MinIOStorageService.Data.Entities;

/// <summary>
/// 文件记录 - 逻辑文件实体，包含多个版本
/// </summary>
public class FileRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// 文件名（逻辑名称，各版本共享）
    /// </summary>
    public string FileName { get; set; } = string.Empty;
    
    /// <summary>
    /// 当前最新版本ID
    /// </summary>
    public Guid? CurrentVersionId { get; set; }
    
    /// <summary>
    /// 内容类型
    /// </summary>
    public string ContentType { get; set; } = string.Empty;
    
    /// <summary>
    /// 版本数量
    /// </summary>
    public int VersionCount { get; set; }
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 描述
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// 标签
    /// </summary>
    public string? Tags { get; set; }
    
    /// <summary>
    /// 是否软删除
    /// </summary>
    public bool IsDeleted { get; set; }
    
    /// <summary>
    /// 删除时间
    /// </summary>
    public DateTime? DeletedAt { get; set; }
    
    // 导航属性
    public ICollection<FileVersion> Versions { get; set; } = new List<FileVersion>();
    public FileVersion? CurrentVersion { get; set; }
}
