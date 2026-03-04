using Microsoft.AspNetCore.SignalR;
using MinIOStorageService.Services;
using System.Text.Json;

namespace MinIOStorageService.Hubs;

/// <summary>
/// P2P Tracker Hub - 协调Peer发现和信令交换
/// </summary>
public class P2PTrackerHub : Hub
{
    private readonly IP2PDownloadService _p2pService;
    private readonly ILogger<P2PTrackerHub> _logger;

    public P2PTrackerHub(IP2PDownloadService p2pService, ILogger<P2PTrackerHub> logger)
    {
        _p2pService = p2pService;
        _logger = logger;
    }

    /// <summary>
    /// Peer加入P2P会话
    /// </summary>
    public async Task JoinSession(string sessionId, string peerId, bool supportsWebRTC = false)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                await Clients.Caller.SendAsync("Error", new { message = "Invalid session ID" });
                return;
            }

            var ipAddress = Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            
            // 注册Peer
            var peerInfo = await _p2pService.RegisterPeerAsync(
                sessionGuid, 
                peerId, 
                Context.ConnectionId, 
                ipAddress,
                supportsWebRTC);

            // 加入SignalR组
            await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);

            // 通知其他Peer有新节点加入
            await Clients.OthersInGroup(sessionId).SendAsync("PeerJoined", new
            {
                peerId = peerInfo.PeerId,
                ipAddress = peerInfo.IpAddress,
                supportsWebRTC = peerInfo.SupportsWebRTC,
                pieceCount = peerInfo.PieceCount
            });

            // 返回会话信息
            var sessionInfo = await _p2pService.GetSessionAsync(sessionGuid);
            await Clients.Caller.SendAsync("SessionJoined", new
            {
                sessionId,
                peerId,
                totalPieces = sessionInfo?.TotalPieces ?? 0,
                pieceSize = sessionInfo?.PieceSize ?? 0,
                activePeers = sessionInfo?.ActivePeerCount ?? 0,
                completionRate = sessionInfo?.CompletionRate ?? 0
            });

            _logger.LogInformation("Peer {PeerId} joined session {SessionId}", peerId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join session: {SessionId}", sessionId);
            await Clients.Caller.SendAsync("Error", new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新Peer拥有的分片
    /// </summary>
    public async Task UpdatePieces(string sessionId, string peerId, List<int> pieceIndices)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return;

            await _p2pService.UpdatePeerPiecesAsync(sessionGuid, peerId, pieceIndices);

            // 通知其他Peer该节点拥有的分片更新
            await Clients.OthersInGroup(sessionId).SendAsync("PeerPiecesUpdated", new
            {
                peerId,
                pieceIndices,
                pieceCount = pieceIndices.Count
            });

            _logger.LogDebug("Peer {PeerId} updated pieces: {Count} pieces", peerId, pieceIndices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update pieces for peer {PeerId}", peerId);
        }
    }

    /// <summary>
    /// 请求分片下载源（最少优先算法）
    /// </summary>
    public async Task RequestPieceSource(string sessionId, int pieceIndex)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return;

            // 从连接上下文中获取PeerId
            var peerId = Context.Items["PeerId"]?.ToString();
            if (string.IsNullOrEmpty(peerId))
            {
                await Clients.Caller.SendAsync("Error", new { message = "Peer not registered" });
                return;
            }

            var source = await _p2pService.SelectOptimalSourceAsync(sessionGuid, pieceIndex, peerId);

            await Clients.Caller.SendAsync("PieceSourceResponse", new
            {
                pieceIndex,
                sourceType = source.Type.ToString(),
                peerId = source.PeerId,
                peerEndpoint = source.PeerEndpoint,
                serverProxyUrl = source.ServerProxyUrl,
                priority = source.Priority
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get piece source for piece {PieceIndex}", pieceIndex);
            await Clients.Caller.SendAsync("Error", new { message = ex.Message });
        }
    }

    /// <summary>
    /// 获取Peer列表
    /// </summary>
    public async Task GetPeers(string sessionId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return;

            var peerId = Context.Items["PeerId"]?.ToString();
            var peers = await _p2pService.GetPeersAsync(sessionGuid, peerId);

            await Clients.Caller.SendAsync("PeersList", peers.Select(p => new
            {
                p.PeerId,
                p.IpAddress,
                p.PieceCount,
                p.UploadSpeed,
                p.DownloadSpeed,
                p.Status,
                p.SupportsWebRTC
            }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get peers for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Peer心跳
    /// </summary>
    public async Task Heartbeat(string sessionId, string peerId, long uploadSpeed, long downloadSpeed)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return;

            await _p2pService.PeerHeartbeatAsync(sessionGuid, peerId, uploadSpeed, downloadSpeed);

            // 存储PeerId到连接上下文
            Context.Items["PeerId"] = peerId;
            Context.Items["SessionId"] = sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heartbeat failed for peer {PeerId}", peerId);
        }
    }

    /// <summary>
    /// WebRTC信令交换 - Offer
    /// </summary>
    public async Task SendWebRTCOffer(string targetPeerId, object offer)
    {
        await Clients.Group(targetPeerId).SendAsync("WebRTCOffer", new
        {
            fromPeerId = Context.Items["PeerId"],
            offer
        });
    }

    /// <summary>
    /// WebRTC信令交换 - Answer
    /// </summary>
    public async Task SendWebRTCAnswer(string targetPeerId, object answer)
    {
        await Clients.Group(targetPeerId).SendAsync("WebRTCAnswer", new
        {
            fromPeerId = Context.Items["PeerId"],
            answer
        });
    }

    /// <summary>
    /// WebRTC信令交换 - ICE Candidate
    /// </summary>
    public async Task SendWebRTCIceCandidate(string targetPeerId, object candidate)
    {
        await Clients.Group(targetPeerId).SendAsync("WebRTCIceCandidate", new
        {
            fromPeerId = Context.Items["PeerId"],
            candidate
        });
    }

    /// <summary>
    /// 离开会话
    /// </summary>
    public async Task LeaveSession(string sessionId, string peerId)
    {
        try
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid)) return;

            await _p2pService.LeaveSessionAsync(sessionGuid, peerId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);

            // 通知其他Peer
            await Clients.OthersInGroup(sessionId).SendAsync("PeerLeft", new { peerId });

            _logger.LogInformation("Peer {PeerId} left session {SessionId}", peerId, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave session for peer {PeerId}", peerId);
        }
    }

    /// <summary>
    /// 连接断开时清理
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var peerId = Context.Items["PeerId"]?.ToString();
            var sessionId = Context.Items["SessionId"]?.ToString();

            if (!string.IsNullOrEmpty(peerId) && !string.IsNullOrEmpty(sessionId))
            {
                if (Guid.TryParse(sessionId, out var sessionGuid))
                {
                    await _p2pService.LeaveSessionAsync(sessionGuid, peerId);
                    await Clients.OthersInGroup(sessionId).SendAsync("PeerLeft", new { peerId });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect cleanup");
        }

        await base.OnDisconnectedAsync(exception);
    }
}
