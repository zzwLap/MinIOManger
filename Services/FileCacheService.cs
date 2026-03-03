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

        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<FileVersion> UploadAndRecordAsync(
        IFormFile file, 
        string? folder = null, 
        string? description = null, 
        string? tags = null,
        string? changeDescription = null,
        CancellationToken cancellationToken = default)
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

        // 查找是否已存在同名文件
        var existingRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.FileName == file.FileName && !f.IsDeleted, cancellationToken);

        FileRecord fileRecord;
        int newVersionNumber = 1;

        if (existingRecord != null)
        {
            // 已存在，创建新版本
            fileRecord = existingRecord;
            newVersionNumber = fileRecord.Versions.Where(v => !v.IsDeleted).Max(v => v.VersionNumber) + 1;
            
            // 重置旧版本的 IsLatest
            foreach (var v in fileRecord.Versions.Where(v => v.IsLatest))
            {
                v.IsLatest = false;
            }
            
            // 更新文件记录
            fileRecord.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(description)) fileRecord.Description = description;
            if (!string.IsNullOrEmpty(tags)) fileRecord.Tags = tags;
        }
        else
        {
            // 创建新的文件记录
            fileRecord = new FileRecord
            {
                FileName = file.FileName,
                ContentType = file.ContentType,
                Description = description,
                Tags = tags
            };
            _dbContext.FileRecords.Add(fileRecord);
        }

        // 构建对象名称（包含版本号）
        var fileName = $"{Guid.NewGuid()}_{file.FileName}";
        var objectName = string.IsNullOrEmpty(folder) 
            ? fileName 
            : $"{folder.TrimEnd('/')}/{fileName}";

        // 上传到存储提供者
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

        // 创建版本记录
        var fileVersion = new FileVersion
        {
            FileRecordId = fileRecord.Id,
            VersionNumber = newVersionNumber,
            ObjectName = objectName,
            FileHash = fileHash,
            Size = file.Length,
            ChangeDescription = changeDescription ?? (newVersionNumber == 1 ? "初始版本" : $"版本 {newVersionNumber}"),
            IsLatest = true,
            LocalCachePath = localPath,
            IsCachedLocally = true
        };

        _dbContext.FileVersions.Add(fileVersion);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 更新文件记录的当前版本和版本数
        fileRecord.CurrentVersionId = fileVersion.Id;
        fileRecord.VersionCount = fileRecord.Versions.Count(v => !v.IsDeleted);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("File uploaded and recorded: {FileId} v{Version}, Hash: {Hash}", 
            fileRecord.Id, newVersionNumber, fileHash);
        
        return fileVersion;
    }

    public async Task<(Stream Stream, FileVersion Version)> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _dbContext.FileRecords
            .Include(f => f.CurrentVersion)
            .FirstOrDefaultAsync(f => f.Id == fileId && !f.IsDeleted, cancellationToken);

        if (fileRecord == null)
        {
            throw new FileNotFoundException($"File record not found: {fileId}");
        }

        if (fileRecord.CurrentVersion == null)
        {
            throw new FileNotFoundException($"No active version found for file: {fileId}");
        }

        return await GetVersionStreamAsync(fileRecord.CurrentVersion, cancellationToken);
    }

    public async Task<(Stream Stream, string FileName)> BatchDownloadAsync(List<Guid> fileIds, CancellationToken cancellationToken = default)
    {
        var fileRecords = await _dbContext.FileRecords
            .Include(f => f.CurrentVersion)
            .Where(f => fileIds.Contains(f.Id) && !f.IsDeleted)
            .ToListAsync(cancellationToken);

        if (fileRecords.Count == 0)
        {
            throw new FileNotFoundException("No files found for the given IDs");
        }

        // 确保所有文件的最新版本都已缓存
        foreach (var record in fileRecords)
        {
            if (record.CurrentVersion == null) continue;
            
            if (!File.Exists(record.CurrentVersion.LocalCachePath) || 
                !await VerifyLocalCacheAsync(record.CurrentVersion, cancellationToken))
            {
                await DownloadAndCacheAsync(record.CurrentVersion, cancellationToken);
            }
        }

        // 创建 ZIP 文件
        var memoryStream = new MemoryStream();
        using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var record in fileRecords)
            {
                if (record.CurrentVersion == null) continue;
                
                cancellationToken.ThrowIfCancellationRequested();
                
                var entry = zipArchive.CreateEntry(record.FileName, CompressionLevel.Optimal);
                await using (var entryStream = entry.Open())
                await using (var fileStream = File.OpenRead(record.CurrentVersion.LocalCachePath!))
                {
                    await fileStream.CopyToAsync(entryStream, cancellationToken);
                }
            }
        }

        memoryStream.Position = 0;
        var zipFileName = $"batch_download_{DateTime.Now:yyyyMMddHHmmss}.zip";
        
        return (memoryStream, zipFileName);
    }

    public async Task<bool> SyncFileAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.FileVersions.FindAsync(new object[] { versionId }, cancellationToken);
        if (version == null || version.IsDeleted) return false;

        // 检查本地缓存是否存在
        if (!File.Exists(version.LocalCachePath))
        {
            _logger.LogInformation("Local cache missing for version: {VersionId}, downloading...", versionId);
            await DownloadAndCacheAsync(version, cancellationToken);
            return true;
        }

        // 验证哈希
        if (await VerifyLocalCacheAsync(version, cancellationToken))
        {
            _logger.LogDebug("Local cache is up to date for version: {VersionId}", versionId);
            return true;
        }

        // 哈希不匹配，重新下载
        _logger.LogInformation("Local cache outdated for version: {VersionId}, re-downloading...", versionId);
        await DownloadAndCacheAsync(version, cancellationToken);
        return true;
    }

    public async Task<bool> RefreshCacheAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.FileVersions.FindAsync(new object[] { versionId }, cancellationToken);
        if (version == null || version.IsDeleted) return false;

        await DownloadAndCacheAsync(version, cancellationToken);
        return true;
    }

    public async Task<List<FileRecord>> GetFileRecordsAsync(string? search = null, string? tags = null, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.FileRecords.AsQueryable();

        if (!includeDeleted)
        {
            query = query.Where(f => !f.IsDeleted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(f => f.FileName.Contains(search) || 
                                     (f.Description != null && f.Description.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(tags))
        {
            query = query.Where(f => f.Tags != null && f.Tags.Contains(tags));
        }

        return await query
            .Include(f => f.CurrentVersion)
            .OrderByDescending(f => f.UpdatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<FileRecord?> GetFileRecordAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FileRecords
            .Include(f => f.Versions.Where(v => !v.IsDeleted).OrderByDescending(v => v.VersionNumber))
            .Include(f => f.CurrentVersion)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
    }

    public async Task<bool> SoftDeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (fileRecord == null || fileRecord.IsDeleted) return false;

        fileRecord.IsDeleted = true;
        fileRecord.DeletedAt = DateTime.UtcNow;
        fileRecord.UpdatedAt = DateTime.UtcNow;

        // 软删除所有版本
        foreach (var version in fileRecord.Versions.Where(v => !v.IsDeleted))
        {
            version.IsDeleted = true;
            version.DeletedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("File soft deleted: {FileId}", fileId);
        return true;
    }

    public async Task<bool> UndeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (fileRecord == null || !fileRecord.IsDeleted) return false;

        fileRecord.IsDeleted = false;
        fileRecord.DeletedAt = null;
        fileRecord.UpdatedAt = DateTime.UtcNow;

        // 恢复所有版本
        foreach (var version in fileRecord.Versions.Where(v => v.IsDeleted))
        {
            version.IsDeleted = false;
            version.DeletedAt = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("File undeleted: {FileId}", fileId);
        return true;
    }

    public async Task<bool> PermanentlyDeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);

        if (fileRecord == null) return false;

        try
        {
            // 删除所有版本的存储对象和缓存
            foreach (var version in fileRecord.Versions)
            {
                await _storageProvider.DeleteAsync(version.ObjectName, cancellationToken);
                
                if (!string.IsNullOrEmpty(version.LocalCachePath) && File.Exists(version.LocalCachePath))
                {
                    File.Delete(version.LocalCachePath);
                }
            }

            // 删除数据库记录
            _dbContext.FileRecords.Remove(fileRecord);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("File and all versions permanently deleted: {FileId}", fileId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to permanently delete file: {FileId}", fileId);
            return false;
        }
    }

    public async Task<int> CleanExpiredCacheAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoffTime = DateTime.UtcNow - maxAge;
        var expiredVersions = await _dbContext.FileVersions
            .Where(v => v.IsCachedLocally && v.CreatedAt < cutoffTime && !v.IsLatest)
            .ToListAsync(cancellationToken);

        int cleanedCount = 0;
        foreach (var version in expiredVersions)
        {
            if (!string.IsNullOrEmpty(version.LocalCachePath) && File.Exists(version.LocalCachePath))
            {
                File.Delete(version.LocalCachePath);
                cleanedCount++;
            }

            version.IsCachedLocally = false;
            version.LocalCachePath = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Cleaned {Count} expired cache files", cleanedCount);
        
        return cleanedCount;
    }

    #region 私有辅助方法

    private async Task<(Stream Stream, FileVersion Version)> GetVersionStreamAsync(FileVersion version, CancellationToken cancellationToken)
    {
        // 检查本地缓存
        if (!string.IsNullOrEmpty(version.LocalCachePath) && File.Exists(version.LocalCachePath))
        {
            var localHash = await CalculateFileHashAsync(version.LocalCachePath, cancellationToken);
            if (localHash.Equals(version.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Using local cache for version: {VersionId}", version.Id);
                var localStream = File.OpenRead(version.LocalCachePath);
                return (localStream, version);
            }
            
            _logger.LogWarning("Local cache hash mismatch for version: {VersionId}, will re-download", version.Id);
        }

        // 从存储下载并更新缓存
        await DownloadAndCacheAsync(version, cancellationToken);
        
        var stream = File.OpenRead(version.LocalCachePath!);
        return (stream, version);
    }

    private async Task DownloadAndCacheAsync(FileVersion version, CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(_cacheDirectory, $"{Guid.NewGuid()}.tmp");
        
        try
        {
            // 从存储提供者下载到临时文件
            using (var fileStream = File.Create(tempPath))
            {
                await _storageProvider.DownloadAsync(version.ObjectName, fileStream, cancellationToken);
            }

            // 计算下载文件的哈希
            var downloadedHash = await CalculateFileHashAsync(tempPath, cancellationToken);

            // 验证哈希
            if (!downloadedHash.Equals(version.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Downloaded file hash mismatch for version: {VersionId}. Expected: {Expected}, Got: {Got}", 
                    version.Id, version.FileHash, downloadedHash);
                // 更新数据库中的哈希
                version.FileHash = downloadedHash;
            }

            // 移动到最终位置（使用哈希作为文件名）
            var finalPath = Path.Combine(_cacheDirectory, downloadedHash);
            if (File.Exists(finalPath))
            {
                File.Delete(tempPath);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }

            // 更新版本记录
            version.LocalCachePath = finalPath;
            version.IsCachedLocally = true;
            
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Version cached: {VersionId}, Hash: {Hash}", version.Id, downloadedHash);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
            throw;
        }
    }

    private async Task<bool> VerifyLocalCacheAsync(FileVersion version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(version.LocalCachePath) || !File.Exists(version.LocalCachePath)) 
            return false;

        var localHash = await CalculateFileHashAsync(version.LocalCachePath, cancellationToken);
        return localHash.Equals(version.FileHash, StringComparison.OrdinalIgnoreCase);
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
