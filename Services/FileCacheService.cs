using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data;
using MinIOStorageService.Data.Entities;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MinIOStorageService.Services;

public class FileCacheService : IFileCacheService
{
    private readonly FileDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly string _cacheDirectory;
    private readonly ILogger<FileCacheService> _logger;

    public FileCacheService(
        FileDbContext dbContext,
        IStorageProvider storageProvider,
        ILogger<FileCacheService> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FileCache");
        _logger = logger;

        // 确保缓存目录存在
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<FileRecord> UploadAndRecordAsync(IFormFile file, string? folder = null, string? description = null, string? tags = null, CancellationToken cancellationToken = default)
    {
        // 计算文件哈希
        string fileHash;
        using (var sha256 = SHA256.Create())
        using (var stream = file.OpenReadStream())
        {
            var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
            fileHash = Convert.ToHexString(hash);
            stream.Position = 0;
        }

        // 构建对象名称
        var fileName = $"{Guid.NewGuid()}_{file.FileName}";
        var objectName = string.IsNullOrEmpty(folder) 
            ? fileName 
            : $"{folder.TrimEnd('/')}/{fileName}";

        // 上传到存储提供者（MinIO 或本地文件系统）
        using (var stream = file.OpenReadStream())
        {
            await _storageProvider.UploadAsync(objectName, stream, file.ContentType, cancellationToken);
        }

        // 保存到本地缓存
        var localPath = Path.Combine(_cacheDirectory, fileHash);
        using (var stream = file.OpenReadStream())
        using (var fileStream = File.Create(localPath))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        // 创建数据库记录
        var record = new FileRecord
        {
            FileName = file.FileName,
            ObjectName = objectName,
            ContentType = file.ContentType,
            Size = file.Length,
            FileHash = fileHash,
            LocalCachePath = localPath,
            IsCachedLocally = true,
            LastSyncedAt = DateTime.UtcNow,
            Description = description,
            Tags = tags
        };

        _dbContext.FileRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("File uploaded and recorded: {FileId}, Hash: {Hash}", record.Id, fileHash);
        return record;
    }

    public async Task<(Stream Stream, FileRecord Record)> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FileRecords.FindAsync(new object[] { fileId }, cancellationToken);
        if (record == null)
        {
            throw new FileNotFoundException($"File record not found: {fileId}");
        }

        // 检查本地缓存是否存在且有效
        if (File.Exists(record.LocalCachePath))
        {
            // 验证本地文件哈希
            var localHash = await CalculateFileHashAsync(record.LocalCachePath, cancellationToken);
            
            if (localHash.Equals(record.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Using local cache for file: {FileId}", fileId);
                var localStream = File.OpenRead(record.LocalCachePath);
                return (localStream, record);
            }
            
            _logger.LogWarning("Local cache hash mismatch for file: {FileId}, will re-download", fileId);
        }

        // 从 MinIO 下载并更新缓存
        await DownloadAndCacheAsync(record, cancellationToken);
        
        var stream = File.OpenRead(record.LocalCachePath!);
        return (stream, record);
    }

    public async Task<(Stream Stream, string FileName)> BatchDownloadAsync(List<Guid> fileIds, CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.FileRecords
            .Where(f => fileIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        if (records.Count == 0)
        {
            throw new FileNotFoundException("No files found for the given IDs");
        }

        // 确保所有文件都已缓存
        foreach (var record in records)
        {
            if (!File.Exists(record.LocalCachePath) || 
                !await VerifyLocalCacheAsync(record, cancellationToken))
            {
                await DownloadAndCacheAsync(record, cancellationToken);
            }
        }

        // 创建 ZIP 文件
        var memoryStream = new MemoryStream();
        using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var entry = zipArchive.CreateEntry(record.FileName, CompressionLevel.Optimal);
                await using (var entryStream = entry.Open())
                await using (var fileStream = File.OpenRead(record.LocalCachePath!))
                {
                    await fileStream.CopyToAsync(entryStream, cancellationToken);
                }
            }
        }

        memoryStream.Position = 0;
        var zipFileName = $"batch_download_{DateTime.Now:yyyyMMddHHmmss}.zip";
        
        return (memoryStream, zipFileName);
    }

    public async Task<bool> SyncFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FileRecords.FindAsync(new object[] { fileId }, cancellationToken);
        if (record == null) return false;

        // 检查本地缓存是否存在
        if (!File.Exists(record.LocalCachePath))
        {
            _logger.LogInformation("Local cache missing for file: {FileId}, downloading...", fileId);
            await DownloadAndCacheAsync(record, cancellationToken);
            return true;
        }

        // 验证哈希
        if (await VerifyLocalCacheAsync(record, cancellationToken))
        {
            _logger.LogDebug("Local cache is up to date for file: {FileId}", fileId);
            return true;
        }

        // 哈希不匹配，重新下载
        _logger.LogInformation("Local cache outdated for file: {FileId}, re-downloading...", fileId);
        await DownloadAndCacheAsync(record, cancellationToken);
        return true;
    }

    public async Task<bool> RefreshCacheAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FileRecords.FindAsync(new object[] { fileId }, cancellationToken);
        if (record == null) return false;

        await DownloadAndCacheAsync(record, cancellationToken);
        return true;
    }

    public async Task<List<FileRecord>> GetFileRecordsAsync(string? search = null, string? tags = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.FileRecords.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(f => f.FileName.Contains(search) || 
                                     (f.Description != null && f.Description.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(tags))
        {
            query = query.Where(f => f.Tags != null && f.Tags.Contains(tags));
        }

        return await query.OrderByDescending(f => f.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task<bool> DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var record = await _dbContext.FileRecords.FindAsync(new object[] { fileId }, cancellationToken);
        if (record == null) return false;

        try
        {
            // 删除存储中的对象
            await _storageProvider.DeleteAsync(record.ObjectName, cancellationToken);

            // 删除本地缓存
            if (File.Exists(record.LocalCachePath))
            {
                File.Delete(record.LocalCachePath);
            }

            // 删除数据库记录
            _dbContext.FileRecords.Remove(record);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("File deleted: {FileId}", fileId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file: {FileId}", fileId);
            return false;
        }
    }

    public async Task<int> CleanExpiredCacheAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow - maxAge;
        var expiredRecords = await _dbContext.FileRecords
            .Where(f => f.IsCachedLocally && f.LastSyncedAt < cutoffTime)
            .ToListAsync(cancellationToken);

        int cleanedCount = 0;
        foreach (var record in expiredRecords)
        {
            if (File.Exists(record.LocalCachePath))
            {
                File.Delete(record.LocalCachePath);
                cleanedCount++;
            }

            record.IsCachedLocally = false;
            record.LocalCachePath = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Cleaned {Count} expired cache files", cleanedCount);
        
        return cleanedCount;
    }

    #region 私有辅助方法

    private async Task DownloadAndCacheAsync(FileRecord record, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(_cacheDirectory, $"{Guid.NewGuid()}.tmp");
        
        try
        {
            // 从存储提供者下载到临时文件
            using (var fileStream = File.Create(tempPath))
            {
                await _storageProvider.DownloadAsync(record.ObjectName, fileStream, cancellationToken);
            }

            // 计算下载文件的哈希
            var downloadedHash = await CalculateFileHashAsync(tempPath, cancellationToken);

            // 验证哈希（可选：如果 MinIO 支持 ETag 验证）
            if (!downloadedHash.Equals(record.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Downloaded file hash mismatch for: {FileId}. Expected: {Expected}, Got: {Got}", 
                    record.Id, record.FileHash, downloadedHash);
                // 更新数据库中的哈希
                record.FileHash = downloadedHash;
            }

            // 移动到最终位置（使用哈希作为文件名）
            var finalPath = Path.Combine(_cacheDirectory, downloadedHash);
            if (File.Exists(finalPath))
            {
                File.Delete(tempPath); // 已存在相同文件，删除临时文件
            }
            else
            {
                File.Move(tempPath, finalPath);
            }

            // 更新数据库记录
            record.LocalCachePath = finalPath;
            record.IsCachedLocally = true;
            record.LastSyncedAt = DateTime.UtcNow;
            record.UpdatedAt = DateTime.UtcNow;
            
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("File cached: {FileId}, Hash: {Hash}", record.Id, downloadedHash);
        }
        catch
        {
            // 清理临时文件
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }
    }

    private async Task<bool> VerifyLocalCacheAsync(FileRecord record, CancellationToken cancellationToken)
    {
        if (!File.Exists(record.LocalCachePath)) return false;

        var localHash = await CalculateFileHashAsync(record.LocalCachePath, cancellationToken);
        return localHash.Equals(record.FileHash, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    #endregion
}
