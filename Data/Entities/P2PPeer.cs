namespace MinIOStorageService.Data.Entities;

/// <summary>
/// P2P节点（Peer）- 参与P2P下载的客户端
/// </summary>
public class P2PPeer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// 所属会话ID
    /// </summary>
    public Guid SessionId { get; set; }
    
    /// <summary>
    /// Peer唯一标识（客户端生成）
    /// </summary>
    public string PeerId { get; set; } = string.Empty;
    
    /// <summary>
    /// 客户端IP地址
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// SignalR连接ID
    /// </summary>
    public string ConnectionId { get; set; } = string.Empty;
    
    /// <summary>
    /// 拥有的分片索引列表（JSON数组）
    /// </summary>
    public string AvailablePieces { get; set; } = "[]";
    
    /// <summary>
    /// 上传速度（字节/秒）
    /// </summary>
    public long UploadSpeed { get; set; }
    
    /// <summary>
    /// 下载速度（字节/秒）
    /// </summary>
    public long DownloadSpeed { get; set; }
    
    /// <summary>
    /// Peer状态
    /// </summary>
    public PeerStatus Status { get; set; } = PeerStatus.Active;
    
    /// <summary>
    /// 最后心跳时间
    /// </summary>
    public DateTime LastHeartbeatAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 加入时间
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// 是否支持WebRTC
    /// </summary>
    public bool SupportsWebRTC { get; set; } = false;
    
    /// <summary>
    /// WebRTC信令数据（临时存储）
    /// </summary>
    public string? WebRTCSignalData { get; set; }
    
    // 导航属性
    public P2PDownloadSession Session { get; set; } = null!;
}

/// <summary>
/// Peer状态
/// </summary>
public enum PeerStatus
{
    Active = 0,      // 活跃
    Idle = 1,        // 空闲
    Busy = 2,        // 忙碌
    Offline = 3,     // 离线
    Banned = 4       // 被禁
}
