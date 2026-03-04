using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data;
using MinIOStorageService.Data.Entities;
using System.Security.Cryptography;
using System.Text.Json;

namespace MinIOStorageService.Services;

/// <summary>
/// P2P下载服务实现
/// </summary>
public class P2PDownloadService : IP2PDownloadService
{
    private readonly FileDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<P2PDownloadService> _logger;

    public P2PDownloadService(
        FileDbContext dbContext,
        IStorageProvider storageProvider,
        ILogger<P2PDownloadService> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _logger = logger;
    }

    public async Task<P2PDownloadSession> CreateSessionAsync(Guid fileVersionId, long pieceSize = 2 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        // 获取文件版本信息
        var fileVersion = await _dbContext.FileVersions
            .Include(v => v.FileRecord)
            .FirstOrDefaultAsync(v => v.Id == fileVersionId && !v.IsDeleted, cancellationToken);

        if (fileVersion == null)
        {
            throw new FileNotFoundException($"File version not found: {fileVersionId}");
        }

        // 获取文件大小
        var fileSize = await _storageProvider.GetFileSizeAsync(fileVersion.ObjectName, cancellationToken);
        var totalPieces = (int)Math.Ceiling((double)fileSize / pieceSize);

        // 计算每个分片的哈希（用于校验）
        var pieceHashes = await CalculatePieceHashesAsync(fileVersion.ObjectName, fileSize, pieceSize, cancellationToken);

        // 创建会话
        var session = new P2PDownloadSession
        {
            FileVersionId = fileVersionId,
            FileSize = fileSize,
            PieceSize = pieceSize,
            TotalPieces = totalPieces,
            PieceHashes = JsonSerializer.Serialize(pieceHashes),
            Status = P2PSessionStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _dbContext.P2PDownloadSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("P2P session created: {SessionId}, File: {FileName}, Pieces: {TotalPieces}",
            session.Id, fileVersion.FileRecord?.FileName ?? "unknown", totalPieces);

        return session;
    }

    public async Task<P2PSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.P2PDownloadSessions
            .Include(s => s.FileVersion)
            .ThenInclude(v => v.FileRecord)
            .Include(s => s.Peers)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null) return null;

        // 统计已完成的分片（通过Peer拥有的分片并集）
        var allPieces = new HashSet<int>();
        foreach (var peer in session.Peers.Where(p => p.Status == PeerStatus.Active))
        {
            var pieces = JsonSerializer.Deserialize<List<int>>(peer.AvailablePieces) ?? new List<int>();
            allPieces.UnionWith(pieces);
        }

        return new P2PSessionInfo
        {
            SessionId = session.Id,
            FileVersionId = session.FileVersionId,
            FileName = session.FileVersion?.FileRecord?.FileName ?? "unknown",
            FileSize = session.FileSize,
            PieceSize = session.PieceSize,
            TotalPieces = session.TotalPieces,
            Status = session.Status,
            CreatedAt = session.CreatedAt,
            ExpiresAt = session.ExpiresAt,
            ActivePeerCount = session.Peers.Count(p => p.Status == PeerStatus.Active),
            CompletedPieces = allPieces.Count
        };
    }

    public async Task<P2PPeerInfo> RegisterPeerAsync(Guid sessionId, string peerId, string connectionId, string ipAddress, bool supportsWebRTC = false, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.P2PDownloadSessions
            .Include(s => s.Peers)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new FileNotFoundException($"P2P session not found: {sessionId}");
        }

        // 检查是否已存在该Peer
        var existingPeer = session.Peers.FirstOrDefault(p => p.PeerId == peerId);
        if (existingPeer != null)
        {
            // 更新连接信息
            existingPeer.ConnectionId = connectionId;
            existingPeer.IpAddress = ipAddress;
            existingPeer.Status = PeerStatus.Active;
            existingPeer.LastHeartbeatAt = DateTime.UtcNow;
            existingPeer.SupportsWebRTC = supportsWebRTC;
        }
        else
        {
            // 创建新Peer
            existingPeer = new P2PPeer
            {
                SessionId = sessionId,
                PeerId = peerId,
                ConnectionId = connectionId,
                IpAddress = ipAddress,
                Status = PeerStatus.Active,
                SupportsWebRTC = supportsWebRTC,
                AvailablePieces = "[]",
                LastHeartbeatAt = DateTime.UtcNow
            };
            _dbContext.P2PPeers.Add(existingPeer);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Peer registered: {PeerId} in session {SessionId}", peerId, sessionId);

        return MapToPeerInfo(existingPeer);
    }

    public async Task UpdatePeerPiecesAsync(Guid sessionId, string peerId, List<int> pieceIndices, CancellationToken cancellationToken = default)
    {
        var peer = await _dbContext.P2PPeers
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.PeerId == peerId, cancellationToken);

        if (peer == null)
        {
            throw new FileNotFoundException($"Peer not found: {peerId}");
        }

        peer.AvailablePieces = JsonSerializer.Serialize(pieceIndices);
        peer.LastHeartbeatAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Peer {PeerId} updated pieces: {Count} pieces", peerId, pieceIndices.Count);
    }

    public async Task<List<P2PPeerInfo>> GetPeersAsync(Guid sessionId, string? excludePeerId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.P2PPeers
            .Where(p => p.SessionId == sessionId && p.Status == PeerStatus.Active);

        if (!string.IsNullOrEmpty(excludePeerId))
        {
            query = query.Where(p => p.PeerId != excludePeerId);
        }

        var peers = await query.ToListAsync(cancellationToken);
        return peers.Select(MapToPeerInfo).ToList();
    }

    public async Task<DownloadSourceInfo> SelectOptimalSourceAsync(Guid sessionId, int pieceIndex, string requesterPeerId, CancellationToken cancellationToken = default)
    {
        // 获取所有拥有该分片的Peer
        var peers = await _dbContext.P2PPeers
            .Where(p => p.SessionId == sessionId && 
                       p.Status == PeerStatus.Active && 
                       p.PeerId != requesterPeerId)
            .ToListAsync(cancellationToken);

        var peersWithPiece = peers
            .Select(p => new
            {
                Peer = p,
                Pieces = JsonSerializer.Deserialize<List<int>>(p.AvailablePieces) ?? new List<int>()
            })
            .Where(x => x.Pieces.Contains(pieceIndex))
            .ToList();

        if (peersWithPiece.Any())
        {
            // 最少优先：选择拥有该分片数量最少的Peer（减轻热门Peer压力）
            var selected = peersWithPiece
                .OrderBy(x => x.Pieces.Count)  // 最少优先
                .ThenByDescending(x => x.Peer.UploadSpeed)  // 上传速度优先
                .First();

            return new DownloadSourceInfo
            {
                Type = SourceType.Peer,
                PeerId = selected.Peer.PeerId,
                PeerEndpoint = $"/api/p2p/peers/{selected.Peer.PeerId}/pieces/{pieceIndex}",
                Priority = 1,
                EstimatedSpeed = selected.Peer.UploadSpeed
            };
        }

        // 没有Peer拥有该分片，回退到服务端
        var serverUrl = await GetServerFallbackUrlAsync(sessionId, pieceIndex, cancellationToken);
        return new DownloadSourceInfo
        {
            Type = SourceType.Server,
            ServerProxyUrl = serverUrl,
            Priority = 2,
            EstimatedSpeed = 0
        };
    }

    public async Task<P2PPieceDownloadPlan> GetPieceDownloadPlanAsync(Guid sessionId, string peerId, List<int>? neededPieces = null, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.P2PDownloadSessions
            .Include(s => s.FileVersion)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new FileNotFoundException($"P2P session not found: {sessionId}");
        }

        // 如果没有指定需要的分片，默认全部
        if (neededPieces == null || !neededPieces.Any())
        {
            neededPieces = Enumerable.Range(0, session.TotalPieces).ToList();
        }

        var pieceHashes = JsonSerializer.Deserialize<List<string>>(session.PieceHashes) ?? new List<string>();
        var tasks = new List<PieceDownloadTask>();

        foreach (var pieceIndex in neededPieces)
        {
            if (pieceIndex < 0 || pieceIndex >= session.TotalPieces) continue;

            var source = await SelectOptimalSourceAsync(sessionId, pieceIndex, peerId, cancellationToken);
            var start = (long)pieceIndex * session.PieceSize;
            var end = Math.Min(start + session.PieceSize - 1, session.FileSize - 1);

            tasks.Add(new PieceDownloadTask
            {
                PieceIndex = pieceIndex,
                Start = start,
                End = end,
                Source = source,
                PieceHash = pieceHashes.ElementAtOrDefault(pieceIndex) ?? string.Empty
            });
        }

        // 按优先级排序（P2P优先）
        tasks = tasks.OrderBy(t => t.Source.Priority).ToList();

        return new P2PPieceDownloadPlan
        {
            SessionId = sessionId,
            PeerId = peerId,
            TotalPieces = session.TotalPieces,
            Tasks = tasks
        };
    }

    public async Task PeerHeartbeatAsync(Guid sessionId, string peerId, long uploadSpeed, long downloadSpeed, CancellationToken cancellationToken = default)
    {
        var peer = await _dbContext.P2PPeers
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.PeerId == peerId, cancellationToken);

        if (peer == null) return;

        peer.LastHeartbeatAt = DateTime.UtcNow;
        peer.UploadSpeed = uploadSpeed;
        peer.DownloadSpeed = downloadSpeed;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task LeaveSessionAsync(Guid sessionId, string peerId, CancellationToken cancellationToken = default)
    {
        var peer = await _dbContext.P2PPeers
            .FirstOrDefaultAsync(p => p.SessionId == sessionId && p.PeerId == peerId, cancellationToken);

        if (peer == null) return;

        peer.Status = PeerStatus.Offline;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Peer left session: {PeerId} from {SessionId}", peerId, sessionId);
    }

    public async Task CompleteSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.P2PDownloadSessions
            .FindAsync(new object[] { sessionId }, cancellationToken);

        if (session == null) return;

        session.Status = P2PSessionStatus.Completed;
        session.LastActiveAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("P2P session completed: {SessionId}", sessionId);
    }

    public async Task<(int SessionsCleaned, int PeersCleaned)> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // 清理过期会话
        var expiredSessions = await _dbContext.P2PDownloadSessions
            .Where(s => s.ExpiresAt < now && s.Status != P2PSessionStatus.Expired && s.Status != P2PSessionStatus.Completed)
            .ToListAsync(cancellationToken);

        foreach (var session in expiredSessions)
        {
            session.Status = P2PSessionStatus.Expired;
        }

        // 清理离线Peer（超过5分钟无心跳）
        var offlineThreshold = now.AddMinutes(-5);
        var offlinePeers = await _dbContext.P2PPeers
            .Where(p => p.LastHeartbeatAt < offlineThreshold && p.Status == PeerStatus.Active)
            .ToListAsync(cancellationToken);

        foreach (var peer in offlinePeers)
        {
            peer.Status = PeerStatus.Offline;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cleanup completed: {SessionCount} sessions expired, {PeerCount} peers marked offline",
            expiredSessions.Count, offlinePeers.Count);

        return (expiredSessions.Count, offlinePeers.Count);
    }

    public async Task<string?> GetPieceHashAsync(Guid sessionId, int pieceIndex, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.P2PDownloadSessions
            .FindAsync(new object[] { sessionId }, cancellationToken);

        if (session == null) return null;

        var pieceHashes = JsonSerializer.Deserialize<List<string>>(session.PieceHashes) ?? new List<string>();
        return pieceHashes.ElementAtOrDefault(pieceIndex);
    }

    public async Task<string> GetServerFallbackUrlAsync(Guid sessionId, int pieceIndex, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.P2PDownloadSessions
            .Include(s => s.FileVersion)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new FileNotFoundException($"P2P session not found: {sessionId}");
        }

        var start = (long)pieceIndex * session.PieceSize;
        var end = Math.Min(start + session.PieceSize - 1, session.FileSize - 1);

        // 使用现有的服务端代理下载
        return $"/api/download/proxy/{session.FileVersionId}?start={start}&end={end}";
    }

    #region 私有辅助方法

    private P2PPeerInfo MapToPeerInfo(P2PPeer peer)
    {
        return new P2PPeerInfo
        {
            PeerId = peer.PeerId,
            IpAddress = peer.IpAddress,
            AvailablePieces = JsonSerializer.Deserialize<List<int>>(peer.AvailablePieces) ?? new List<int>(),
            UploadSpeed = peer.UploadSpeed,
            DownloadSpeed = peer.DownloadSpeed,
            Status = peer.Status,
            SupportsWebRTC = peer.SupportsWebRTC,
            LastHeartbeatAt = peer.LastHeartbeatAt
        };
    }

    private async Task<List<string>> CalculatePieceHashesAsync(string objectName, long fileSize, long pieceSize, CancellationToken cancellationToken)
    {
        var hashes = new List<string>();
        var totalPieces = (int)Math.Ceiling((double)fileSize / pieceSize);

        for (int i = 0; i < totalPieces; i++)
        {
            var start = (long)i * pieceSize;
            var end = Math.Min(start + pieceSize - 1, fileSize - 1);

            // 获取分片数据并计算哈希
            using var stream = await _storageProvider.GetRangeStreamAsync(objectName, start, end, cancellationToken);
            using var sha256 = SHA256.Create();
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            hashes.Add(Convert.ToHexString(hash));
        }

        return hashes;
    }

    #endregion
}
