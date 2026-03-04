using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Services;

/// <summary>
/// P2P下载服务接口
/// </summary>
public interface IP2PDownloadService
{
    /// <summary>
    /// 创建P2P下载会话
    /// </summary>
    Task<P2PDownloadSession> CreateSessionAsync(Guid fileVersionId, long pieceSize = 2 * 1024 * 1024, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取会话信息
    /// </summary>
    Task<P2PSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 注册Peer加入会话
    /// </summary>
    Task<P2PPeerInfo> RegisterPeerAsync(Guid sessionId, string peerId, string connectionId, string ipAddress, bool supportsWebRTC = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 更新Peer拥有的分片
    /// </summary>
    Task UpdatePeerPiecesAsync(Guid sessionId, string peerId, List<int> pieceIndices, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取Peer列表（排除指定Peer）
    /// </summary>
    Task<List<P2PPeerInfo>> GetPeersAsync(Guid sessionId, string? excludePeerId = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 选择最优下载源（最少优先算法）
    /// </summary>
    Task<DownloadSourceInfo> SelectOptimalSourceAsync(Guid sessionId, int pieceIndex, string requesterPeerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取分片下载计划（混合模式：P2P优先，服务端兜底）
    /// </summary>
    Task<P2PPieceDownloadPlan> GetPieceDownloadPlanAsync(Guid sessionId, string peerId, List<int>? neededPieces = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Peer心跳
    /// </summary>
    Task PeerHeartbeatAsync(Guid sessionId, string peerId, long uploadSpeed, long downloadSpeed, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Peer离开会话
    /// </summary>
    Task LeaveSessionAsync(Guid sessionId, string peerId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 完成会话
    /// </summary>
    Task CompleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清理过期会话和Peer
    /// </summary>
    Task<(int SessionsCleaned, int PeersCleaned)> CleanupExpiredAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取分片哈希（用于校验）
    /// </summary>
    Task<string?> GetPieceHashAsync(Guid sessionId, int pieceIndex, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取服务端代理URL（P2P失败时回退）
    /// </summary>
    Task<string> GetServerFallbackUrlAsync(Guid sessionId, int pieceIndex, CancellationToken cancellationToken = default);
}

/// <summary>
/// P2P会话信息
/// </summary>
public class P2PSessionInfo
{
    public Guid SessionId { get; set; }
    public Guid FileVersionId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public long PieceSize { get; set; }
    public int TotalPieces { get; set; }
    public P2PSessionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int ActivePeerCount { get; set; }
    public int CompletedPieces { get; set; }
    public double CompletionRate => TotalPieces > 0 ? (CompletedPieces * 100.0 / TotalPieces) : 0;
}

/// <summary>
/// Peer信息
/// </summary>
public class P2PPeerInfo
{
    public string PeerId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public List<int> AvailablePieces { get; set; } = new();
    public long UploadSpeed { get; set; }
    public long DownloadSpeed { get; set; }
    public PeerStatus Status { get; set; }
    public bool SupportsWebRTC { get; set; }
    public DateTime LastHeartbeatAt { get; set; }
    public int PieceCount => AvailablePieces.Count;
}

/// <summary>
/// 下载源信息
/// </summary>
public class DownloadSourceInfo
{
    public SourceType Type { get; set; }
    public string? PeerId { get; set; }
    public string? PeerEndpoint { get; set; }
    public string? ServerProxyUrl { get; set; }
    public int Priority { get; set; }
    public long EstimatedSpeed { get; set; }
}

/// <summary>
/// 下载源类型
/// </summary>
public enum SourceType
{
    Peer = 0,       // P2P节点
    Server = 1      // 服务端代理
}

/// <summary>
/// P2P分片下载计划
/// </summary>
public class P2PPieceDownloadPlan
{
    public Guid SessionId { get; set; }
    public string PeerId { get; set; } = string.Empty;
    public int TotalPieces { get; set; }
    public List<PieceDownloadTask> Tasks { get; set; } = new();
}

/// <summary>
/// 单个分片下载任务
/// </summary>
public class PieceDownloadTask
{
    public int PieceIndex { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public long Size => End - Start + 1;
    public DownloadSourceInfo Source { get; set; } = null!;
    public string PieceHash { get; set; } = string.Empty;
}
