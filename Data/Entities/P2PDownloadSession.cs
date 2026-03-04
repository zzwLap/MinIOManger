namespace MinIOStorageService.Data.Entities;

/// <summary>
/// P2P下载会话 - 管理一个文件的P2P下载
/// </summary>
public class P2PDownloadSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// 文件版本ID
    /// </summary>
    public Guid FileVersionId { get; set; }
    
    /// <summary>
    /// 文件总大小
    /// </summary>
    public long FileSize { get; set; }
    
    /// <summary>
    /// 分片大小
    /// </summary>
    public long PieceSize { get; set; } = 2 * 1024 * 1024; // 默认2MB
    
    /// <summary>
    /// 总分片数
    /// </summary>
    public int TotalPieces { get; set; }
    
    /// <summary>
    /// 分片哈希列表（JSON）
    /// </summary>
    public string PieceHashes { get; set; } = "[]";
    
    /// <summary>
    /// 会话状态
    /// </summary>
    public P2PSessionStatus Status { get; set; } = P2PSessionStatus.Active;
    
    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
    
    /// <summary>
    /// 最后活跃时间
    /// </summary>
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
    
    // 导航属性
    public FileVersion FileVersion { get; set; } = null!;
    public ICollection<P2PPeer> Peers { get; set; } = new List<P2PPeer>();
}

/// <summary>
/// P2P会话状态
/// </summary>
public enum P2PSessionStatus
{
    Active = 0,      // 活跃
    Paused = 1,      // 暂停
    Completed = 2,   // 完成
    Expired = 3,     // 过期
    Cancelled = 4    // 取消
}
