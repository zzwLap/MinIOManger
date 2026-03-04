using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MinIOStorageService.Hubs;
using MinIOStorageService.Services;
using System.ComponentModel.DataAnnotations;

namespace MinIOStorageService.Controllers;

/// <summary>
/// P2P下载控制器 - 管理P2P下载会话和Peer协调
/// </summary>
[ApiController]
[Route("api/p2p")]
public class P2PDownloadController : ControllerBase
{
    private readonly IP2PDownloadService _p2pService;
    private readonly IHubContext<P2PTrackerHub> _hubContext;
    private readonly ILogger<P2PDownloadController> _logger;

    public P2PDownloadController(
        IP2PDownloadService p2pService,
        IHubContext<P2PTrackerHub> hubContext,
        ILogger<P2PDownloadController> logger)
    {
        _p2pService = p2pService;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// 创建P2P下载会话
    /// </summary>
    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateP2PSessionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _p2pService.CreateSessionAsync(
                request.FileVersionId, 
                request.PieceSize, 
                cancellationToken);

            return Ok(new
            {
                message = "P2P会话创建成功",
                sessionId = session.Id,
                fileVersionId = session.FileVersionId,
                fileSize = session.FileSize,
                pieceSize = session.PieceSize,
                totalPieces = session.TotalPieces,
                status = session.Status.ToString(),
                expiresAt = session.ExpiresAt
            });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建P2P会话失败");
            return StatusCode(500, new { message = "创建P2P会话失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取P2P会话信息
    /// </summary>
    [HttpGet("sessions/{sessionId}")]
    public async Task<IActionResult> GetSession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _p2pService.GetSessionAsync(sessionId, cancellationToken);

            if (session == null)
            {
                return NotFound(new { message = "P2P会话不存在", sessionId });
            }

            return Ok(new
            {
                sessionId = session.SessionId,
                fileVersionId = session.FileVersionId,
                fileName = session.FileName,
                fileSize = session.FileSize,
                pieceSize = session.PieceSize,
                totalPieces = session.TotalPieces,
                status = session.Status.ToString(),
                createdAt = session.CreatedAt,
                expiresAt = session.ExpiresAt,
                activePeerCount = session.ActivePeerCount,
                completedPieces = session.CompletedPieces,
                completionRate = Math.Round(session.CompletionRate, 2)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取P2P会话信息失败: {SessionId}", sessionId);
            return StatusCode(500, new { message = "获取P2P会话信息失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取Peer列表
    /// </summary>
    [HttpGet("sessions/{sessionId}/peers")]
    public async Task<IActionResult> GetPeers(Guid sessionId, [FromQuery] string? excludePeerId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var peers = await _p2pService.GetPeersAsync(sessionId, excludePeerId, cancellationToken);

            return Ok(new
            {
                sessionId,
                peerCount = peers.Count,
                peers = peers.Select(p => new
                {
                    p.PeerId,
                    p.IpAddress,
                    p.PieceCount,
                    p.UploadSpeed,
                    p.DownloadSpeed,
                    p.Status,
                    p.SupportsWebRTC,
                    p.LastHeartbeatAt
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取Peer列表失败: {SessionId}", sessionId);
            return StatusCode(500, new { message = "获取Peer列表失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取分片下载计划（混合模式）
    /// </summary>
    [HttpGet("sessions/{sessionId}/peers/{peerId}/plan")]
    public async Task<IActionResult> GetPieceDownloadPlan(
        Guid sessionId, 
        string peerId, 
        [FromQuery] string? neededPieces = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<int>? piecesList = null;
            if (!string.IsNullOrEmpty(neededPieces))
            {
                piecesList = neededPieces.Split(',').Select(int.Parse).ToList();
            }

            var plan = await _p2pService.GetPieceDownloadPlanAsync(sessionId, peerId, piecesList, cancellationToken);

            return Ok(new
            {
                sessionId = plan.SessionId,
                peerId = plan.PeerId,
                totalPieces = plan.TotalPieces,
                tasks = plan.Tasks.Select(t => new
                {
                    t.PieceIndex,
                    t.Start,
                    t.End,
                    t.Size,
                    sourceType = t.Source.Type.ToString(),
                    peerId = t.Source.PeerId,
                    peerEndpoint = t.Source.PeerEndpoint,
                    serverProxyUrl = t.Source.ServerProxyUrl,
                    priority = t.Source.Priority,
                    pieceHash = t.PieceHash
                })
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "P2P会话不存在", sessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取分片下载计划失败: {SessionId}, {PeerId}", sessionId, peerId);
            return StatusCode(500, new { message = "获取分片下载计划失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 选择最优下载源（最少优先算法）
    /// </summary>
    [HttpGet("sessions/{sessionId}/pieces/{pieceIndex}/source")]
    public async Task<IActionResult> SelectOptimalSource(
        Guid sessionId, 
        int pieceIndex, 
        [FromQuery][Required] string requesterPeerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var source = await _p2pService.SelectOptimalSourceAsync(sessionId, pieceIndex, requesterPeerId, cancellationToken);

            return Ok(new
            {
                sessionId,
                pieceIndex,
                sourceType = source.Type.ToString(),
                peerId = source.PeerId,
                peerEndpoint = source.PeerEndpoint,
                serverProxyUrl = source.ServerProxyUrl,
                priority = source.Priority,
                estimatedSpeed = source.EstimatedSpeed
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { message = "P2P会话不存在", sessionId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选择下载源失败: {SessionId}, Piece: {PieceIndex}", sessionId, pieceIndex);
            return StatusCode(500, new { message = "选择下载源失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 从Peer下载分片（HTTP P2P传输）
    /// </summary>
    [HttpGet("peers/{peerId}/pieces/{pieceIndex}")]
    public async Task<IActionResult> DownloadPieceFromPeer(
        string peerId,
        int pieceIndex,
        [FromQuery][Required] Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 这里实际应该转发到对应Peer，或者让Peer主动提供HTTP端点
            // 简化实现：返回该Peer的连接信息，让请求者通过其他方式（如WebRTC）获取
            
            var peers = await _p2pService.GetPeersAsync(sessionId, null, cancellationToken);
            var targetPeer = peers.FirstOrDefault(p => p.PeerId == peerId);

            if (targetPeer == null)
            {
                return NotFound(new { message = "Peer不存在或已离线", peerId });
            }

            var hasPiece = targetPeer.AvailablePieces.Contains(pieceIndex);
            if (!hasPiece)
            {
                return BadRequest(new { message = "Peer不拥有该分片", peerId, pieceIndex });
            }

            // 返回Peer信息，客户端需要通过SignalR或其他方式与该Peer建立连接
            return Ok(new
            {
                message = "请通过SignalR与Peer建立P2P连接",
                peerId = targetPeer.PeerId,
                ipAddress = targetPeer.IpAddress,
                supportsWebRTC = targetPeer.SupportsWebRTC,
                pieceIndex,
                // 提供服务端回退URL
                fallbackUrl = await _p2pService.GetServerFallbackUrlAsync(sessionId, pieceIndex, cancellationToken)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取Peer分片失败: {PeerId}, Piece: {PieceIndex}", peerId, pieceIndex);
            return StatusCode(500, new { message = "获取Peer分片失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取分片哈希（用于校验）
    /// </summary>
    [HttpGet("sessions/{sessionId}/pieces/{pieceIndex}/hash")]
    public async Task<IActionResult> GetPieceHash(Guid sessionId, int pieceIndex, CancellationToken cancellationToken = default)
    {
        try
        {
            var hash = await _p2pService.GetPieceHashAsync(sessionId, pieceIndex, cancellationToken);

            if (hash == null)
            {
                return NotFound(new { message = "分片哈希不存在", sessionId, pieceIndex });
            }

            return Ok(new
            {
                sessionId,
                pieceIndex,
                hash
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取分片哈希失败: {SessionId}, Piece: {PieceIndex}", sessionId, pieceIndex);
            return StatusCode(500, new { message = "获取分片哈希失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 完成P2P会话
    /// </summary>
    [HttpPost("sessions/{sessionId}/complete")]
    public async Task<IActionResult> CompleteSession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _p2pService.CompleteSessionAsync(sessionId, cancellationToken);

            // 通知所有Peer会话已完成
            await _hubContext.Clients.Group(sessionId.ToString()).SendAsync("SessionCompleted", new { sessionId });

            return Ok(new
            {
                message = "P2P会话已完成",
                sessionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "完成P2P会话失败: {SessionId}", sessionId);
            return StatusCode(500, new { message = "完成P2P会话失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 清理过期会话和Peer
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupExpired(CancellationToken cancellationToken = default)
    {
        try
        {
            var (sessionsCleaned, peersCleaned) = await _p2pService.CleanupExpiredAsync(cancellationToken);

            return Ok(new
            {
                message = "清理完成",
                sessionsCleaned,
                peersCleaned
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理过期会话失败");
            return StatusCode(500, new { message = "清理过期会话失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 广播消息给会话中的所有Peer（测试用）
    /// </summary>
    [HttpPost("sessions/{sessionId}/broadcast")]
    public async Task<IActionResult> BroadcastToSession(Guid sessionId, [FromBody] BroadcastRequest request)
    {
        try
        {
            await _hubContext.Clients.Group(sessionId.ToString())
                .SendAsync(request.MessageType, request.Data);

            return Ok(new
            {
                message = "广播成功",
                sessionId,
                messageType = request.MessageType
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "广播消息失败: {SessionId}", sessionId);
            return StatusCode(500, new { message = "广播消息失败", error = ex.Message });
        }
    }
}

#region 请求模型

public class CreateP2PSessionRequest
{
    [Required]
    public Guid FileVersionId { get; set; }
    
    /// <summary>
    /// 分片大小（默认2MB）
    /// </summary>
    public long PieceSize { get; set; } = 2 * 1024 * 1024;
}

public class BroadcastRequest
{
    [Required]
    public string MessageType { get; set; } = string.Empty;
    
    public object? Data { get; set; }
}

#endregion
