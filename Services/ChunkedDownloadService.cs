using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data;
using MinIOStorageService.Data.Entities;
using System.Text.Json;

namespace MinIOStorageService.Services;

/// <summary>
/// 分片下载服务实现 - 服务端代理流方案
/// </summary>
public class ChunkedDownloadService : IChunkedDownloadService
{
    private readonly FileDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly ILogger<ChunkedDownloadService> _logger;

    public ChunkedDownloadService(
        FileDbContext dbContext,
        IStorageProvider storageProvider,
        ILogger<ChunkedDownloadService> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _logger = logger;
    }

    public async Task<DownloadTask> CreateDownloadTaskAsync(Guid fileVersionId, string clientId, CancellationToken cancellationToken = default)
    {
        // 检查文件版本是否存在
        var fileVersion = await _dbContext.FileVersions
            .Include(v => v.FileRecord)
            .FirstOrDefaultAsync(v => v.Id == fileVersionId && !v.IsDeleted, cancellationToken);

        if (fileVersion == null)
        {
            throw new FileNotFoundException($"File version not found: {fileVersionId}");
        }

        // 检查是否已存在进行中的下载任务
        var existingTask = await _dbContext.DownloadTasks
            .FirstOrDefaultAsync(t => 
                t.FileVersionId == fileVersionId && 
                t.ClientId == clientId && 
                t.Status == Data.Entities.DownloadTaskStatus.Downloading &&
                t.ExpiresAt > DateTime.UtcNow, 
                cancellationToken);

        if (existingTask != null)
        {
            _logger.LogInformation("Reusing existing download task: {TaskId}", existingTask.Id);
            return existingTask;
        }

        // 获取文件大小
        var fileSize = await _storageProvider.GetFileSizeAsync(fileVersion.ObjectName, cancellationToken);

        // 创建新的下载任务
        var task = new DownloadTask
        {
            FileVersionId = fileVersionId,
            ClientId = clientId,
            FileSize = fileSize,
            DownloadedBytes = 0,
            Status = Data.Entities.DownloadTaskStatus.Pending,
            DownloadedChunks = "[]",
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        _dbContext.DownloadTasks.Add(task);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Download task created: {TaskId}, File: {FileName}, Size: {FileSize}",
            task.Id, fileVersion.FileRecord?.FileName ?? "unknown", fileSize);

        return task;
    }

    public async Task<DownloadTaskInfo> GetDownloadStatusAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DownloadTasks
            .Include(t => t.FileVersion)
            .ThenInclude(v => v.FileRecord)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task == null)
        {
            throw new FileNotFoundException($"Download task not found: {taskId}");
        }

        // 检查是否过期
        if (task.Status != Data.Entities.DownloadTaskStatus.Completed && 
            task.Status != Data.Entities.DownloadTaskStatus.Cancelled &&
            task.ExpiresAt < DateTime.UtcNow)
        {
            task.Status = Data.Entities.DownloadTaskStatus.Expired;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var downloadedChunks = JsonSerializer.Deserialize<List<int>>(task.DownloadedChunks) ?? new List<int>();

        return new DownloadTaskInfo
        {
            TaskId = task.Id,
            FileVersionId = task.FileVersionId,
            FileName = task.FileVersion?.FileRecord?.FileName ?? "unknown",
            FileSize = task.FileSize,
            DownloadedBytes = task.DownloadedBytes,
            State = (DownloadTaskState)(int)task.Status,
            CreatedAt = task.CreatedAt,
            ExpiresAt = task.ExpiresAt,
            CompletedAt = task.CompletedAt
        };
    }

    public async Task<(Stream Stream, long TotalSize, long Start, long End)> DownloadRangeAsync(
        Guid taskId, 
        long start, 
        long? end = null, 
        CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DownloadTasks
            .Include(t => t.FileVersion)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task == null)
        {
            throw new FileNotFoundException($"Download task not found: {taskId}");
        }

        if (task.Status == Data.Entities.DownloadTaskStatus.Completed)
        {
            throw new InvalidOperationException("Download already completed");
        }

        if (task.Status == Data.Entities.DownloadTaskStatus.Cancelled)
        {
            throw new InvalidOperationException("Download has been cancelled");
        }

        if (task.ExpiresAt < DateTime.UtcNow)
        {
            task.Status = Data.Entities.DownloadTaskStatus.Expired;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Download task has expired");
        }

        // 验证范围
        if (start < 0 || start >= task.FileSize)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Start position is out of range");
        }

        var actualEnd = end ?? task.FileSize - 1;
        if (actualEnd >= task.FileSize)
        {
            actualEnd = task.FileSize - 1;
        }

        // 更新任务状态
        if (task.Status == Data.Entities.DownloadTaskStatus.Pending)
        {
            task.Status = Data.Entities.DownloadTaskStatus.Downloading;
        }
        task.LastUpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 获取范围流
        var stream = await _storageProvider.GetRangeStreamAsync(
            task.FileVersion.ObjectName, 
            start, 
            actualEnd, 
            cancellationToken);

        _logger.LogDebug("Download range: Task={TaskId}, Bytes={Start}-{End}", taskId, start, actualEnd);

        return (stream, task.FileSize, start, actualEnd);
    }

    public async Task<ChunkDownloadPlan> GetChunkDownloadPlanAsync(
        Guid taskId, 
        long chunkSize = 5 * 1024 * 1024, 
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DownloadTasks
            .Include(t => t.FileVersion)
            .ThenInclude(v => v.FileRecord)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task == null)
        {
            throw new FileNotFoundException($"Download task not found: {taskId}");
        }

        var totalChunks = (int)Math.Ceiling((double)task.FileSize / chunkSize);
        var downloadedChunks = JsonSerializer.Deserialize<List<int>>(task.DownloadedChunks) ?? new List<int>();

        var chunks = new List<ChunkInfo>();
        for (int i = 0; i < totalChunks; i++)
        {
            var chunkStart = i * chunkSize;
            var chunkEnd = Math.Min(chunkStart + chunkSize - 1, task.FileSize - 1);
            
            // 生成服务端代理下载URL
            var url = $"/api/download/proxy/{taskId}/chunks/{i}";

            chunks.Add(new ChunkInfo
            {
                Index = i,
                Start = chunkStart,
                End = chunkEnd,
                Url = url
            });
        }

        return new ChunkDownloadPlan
        {
            TaskId = task.Id,
            FileName = task.FileVersion?.FileRecord?.FileName ?? "unknown",
            FileSize = task.FileSize,
            ChunkSize = chunkSize,
            TotalChunks = totalChunks,
            Chunks = chunks
        };
    }

    public async Task UpdateProgressAsync(Guid taskId, long downloadedBytes, CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DownloadTasks.FindAsync(new object[] { taskId }, cancellationToken);
        if (task == null) return;

        task.DownloadedBytes = downloadedBytes;
        task.LastUpdatedAt = DateTime.UtcNow;

        // 如果全部下载完成，自动标记为完成
        if (task.DownloadedBytes >= task.FileSize)
        {
            task.Status = Data.Entities.DownloadTaskStatus.Completed;
            task.CompletedAt = DateTime.UtcNow;
            _logger.LogInformation("Download completed: {TaskId}", taskId);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteDownloadAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DownloadTasks.FindAsync(new object[] { taskId }, cancellationToken);
        if (task == null) return;

        task.Status = Data.Entities.DownloadTaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.DownloadedBytes = task.FileSize;
        task.LastUpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Download marked as completed: {TaskId}", taskId);
    }

    public async Task CancelDownloadAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DownloadTasks.FindAsync(new object[] { taskId }, cancellationToken);
        if (task == null) return;

        if (task.Status == Data.Entities.DownloadTaskStatus.Completed)
        {
            throw new InvalidOperationException("Cannot cancel completed download");
        }

        task.Status = Data.Entities.DownloadTaskStatus.Cancelled;
        task.LastUpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Download cancelled: {TaskId}", taskId);
    }

    public async Task<int> CleanupExpiredTasksAsync(CancellationToken cancellationToken = default)
    {
        var expiredTasks = await _dbContext.DownloadTasks
            .Where(t => t.ExpiresAt < DateTime.UtcNow && 
                       t.Status != Data.Entities.DownloadTaskStatus.Completed && 
                       t.Status != Data.Entities.DownloadTaskStatus.Cancelled &&
                       t.Status != Data.Entities.DownloadTaskStatus.Expired)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var task in expiredTasks)
        {
            task.Status = Data.Entities.DownloadTaskStatus.Expired;
            count++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Cleaned up {Count} expired download tasks", count);
        return count;
    }

    /// <summary>
    /// 下载指定分片（服务端代理）
    /// </summary>
    public async Task<(Stream Stream, ChunkInfo Chunk)> DownloadChunkAsync(
        Guid taskId, 
        int chunkIndex, 
        long chunkSize = 5 * 1024 * 1024,
        CancellationToken cancellationToken = default)
    {
        var task = await _dbContext.DownloadTasks
            .Include(t => t.FileVersion)
            .ThenInclude(v => v.FileRecord)
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task == null)
        {
            throw new FileNotFoundException($"Download task not found: {taskId}");
        }

        var totalChunks = (int)Math.Ceiling((double)task.FileSize / chunkSize);
        if (chunkIndex < 0 || chunkIndex >= totalChunks)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex), "Chunk index is out of range");
        }

        var start = chunkIndex * chunkSize;
        var end = Math.Min(start + chunkSize - 1, task.FileSize - 1);

        // 获取范围流
        var stream = await _storageProvider.GetRangeStreamAsync(
            task.FileVersion.ObjectName, 
            start, 
            end, 
            cancellationToken);

        // 更新下载进度
        var downloadedChunks = JsonSerializer.Deserialize<List<int>>(task.DownloadedChunks) ?? new List<int>();
        if (!downloadedChunks.Contains(chunkIndex))
        {
            downloadedChunks.Add(chunkIndex);
            downloadedChunks.Sort();
            task.DownloadedChunks = JsonSerializer.Serialize(downloadedChunks);
            
            // 计算已下载字节数
            task.DownloadedBytes = Math.Min((long)downloadedChunks.Count * chunkSize, task.FileSize);
            task.LastUpdatedAt = DateTime.UtcNow;

            if (task.Status == Data.Entities.DownloadTaskStatus.Pending)
            {
                task.Status = Data.Entities.DownloadTaskStatus.Downloading;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var chunk = new ChunkInfo
        {
            Index = chunkIndex,
            Start = start,
            End = end,
            Url = $"/api/download/proxy/{taskId}/chunks/{chunkIndex}"
        };

        _logger.LogDebug("Chunk downloaded: Task={TaskId}, Chunk={ChunkIndex}, Bytes={Start}-{End}", 
            taskId, chunkIndex, start, end);

        return (stream, chunk);
    }
}
