using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data;
using MinIOStorageService.Data.Entities;
using System.Security.Cryptography;

namespace MinIOStorageService.Services;

public class FileVersionService : IFileVersionService
{
    private readonly FileDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly string _cacheDirectory;
    private readonly ILogger<FileVersionService> _logger;

    public FileVersionService(
        FileDbContext dbContext,
        IStorageProvider storageProvider,
        ILogger<FileVersionService> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _cacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VersionCache");
        _logger = logger;

        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<List<FileVersion>> GetVersionsAsync(Guid fileRecordId, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.FileVersions
            .Where(v => v.FileRecordId == fileRecordId);

        if (!includeDeleted)
        {
            query = query.Where(v => !v.IsDeleted);
        }

        return await query
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<FileVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FileVersions
            .Include(v => v.FileRecord)
            .FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);
    }

    public async Task<(Stream Stream, FileVersion Version)> DownloadVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await GetVersionAsync(versionId, cancellationToken);
        if (version == null)
        {
            throw new FileNotFoundException($"Version not found: {versionId}");
        }

        if (version.IsDeleted)
        {
            throw new InvalidOperationException($"Version {versionId} has been deleted");
        }

        // 检查本地缓存
        if (!string.IsNullOrEmpty(version.LocalCachePath) && File.Exists(version.LocalCachePath))
        {
            var localHash = await CalculateFileHashAsync(version.LocalCachePath, cancellationToken);
            if (localHash.Equals(version.FileHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Using local cache for version: {VersionId}", versionId);
                var localStream = File.OpenRead(version.LocalCachePath);
                return (localStream, version);
            }
        }

        // 从存储提供者下载
        var stream = await _storageProvider.GetStreamAsync(version.ObjectName, cancellationToken);
        
        // 保存到缓存
        var cachePath = await CacheVersionAsync(version, stream, cancellationToken);
        version.LocalCachePath = cachePath;
        version.IsCachedLocally = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var resultStream = File.OpenRead(cachePath);
        return (resultStream, version);
    }

    public async Task<FileVersion?> RestoreVersionAsync(Guid versionId, string? changeDescription = null, CancellationToken cancellationToken = default)
    {
        var sourceVersion = await GetVersionAsync(versionId, cancellationToken);
        if (sourceVersion == null || sourceVersion.IsDeleted)
        {
            return null;
        }

        var fileRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == sourceVersion.FileRecordId, cancellationToken);

        if (fileRecord == null)
        {
            return null;
        }

        // 下载源版本内容
        var (stream, _) = await DownloadVersionAsync(versionId, cancellationToken);

        // 创建新版本（复制内容）
        var newVersionNumber = fileRecord.Versions.Where(v => !v.IsDeleted).Max(v => v.VersionNumber) + 1;
        var newObjectName = $"{fileRecord.FileName}_v{newVersionNumber}_{Guid.NewGuid()}";

        using (stream)
        {
            await _storageProvider.UploadAsync(newObjectName, stream, fileRecord.ContentType, cancellationToken);
        }

        // 计算哈希
        var (stream2, _) = await DownloadVersionAsync(versionId, cancellationToken);
        string fileHash;
        using (stream2)
        using (var sha256 = SHA256.Create())
        {
            var hash = await sha256.ComputeHashAsync(stream2, cancellationToken);
            fileHash = Convert.ToHexString(hash);
        }

        // 重置旧版本的 IsLatest
        foreach (var v in fileRecord.Versions.Where(v => v.IsLatest))
        {
            v.IsLatest = false;
        }

        // 创建新版本记录
        var newVersion = new FileVersion
        {
            FileRecordId = fileRecord.Id,
            VersionNumber = newVersionNumber,
            ObjectName = newObjectName,
            FileHash = fileHash,
            Size = sourceVersion.Size,
            ChangeDescription = $"从版本 {sourceVersion.VersionNumber} 恢复" + (changeDescription != null ? $": {changeDescription}" : ""),
            IsLatest = true
        };

        _dbContext.FileVersions.Add(newVersion);
        
        // 更新文件记录
        fileRecord.CurrentVersionId = newVersion.Id;
        fileRecord.VersionCount = fileRecord.Versions.Count(v => !v.IsDeleted) + 1;
        fileRecord.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Version restored: {FileId} from version {SourceVersion} to {NewVersion}",
            fileRecord.Id, sourceVersion.VersionNumber, newVersionNumber);

        return newVersion;
    }

    public async Task<bool> SoftDeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.FileVersions.FindAsync(new object[] { versionId }, cancellationToken);
        if (version == null || version.IsDeleted) return false;

        version.IsDeleted = true;
        version.DeletedAt = DateTime.UtcNow;

        // 如果删除的是最新版本，需要更新文件记录的当前版本
        if (version.IsLatest)
        {
            version.IsLatest = false;
            
            var fileRecord = await _dbContext.FileRecords
                .Include(f => f.Versions)
                .FirstOrDefaultAsync(f => f.Id == version.FileRecordId, cancellationToken);

            if (fileRecord != null)
            {
                var newLatest = fileRecord.Versions
                    .Where(v => !v.IsDeleted && v.Id != versionId)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();

                if (newLatest != null)
                {
                    newLatest.IsLatest = true;
                    fileRecord.CurrentVersionId = newLatest.Id;
                }
                else
                {
                    // 没有可用版本了，软删除文件记录
                    fileRecord.IsDeleted = true;
                    fileRecord.DeletedAt = DateTime.UtcNow;
                    fileRecord.CurrentVersionId = null;
                }

                fileRecord.VersionCount = fileRecord.Versions.Count(v => !v.IsDeleted);
                fileRecord.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Version soft deleted: {VersionId}", versionId);
        return true;
    }

    public async Task<bool> UndeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.FileVersions.FindAsync(new object[] { versionId }, cancellationToken);
        if (version == null || !version.IsDeleted) return false;

        version.IsDeleted = false;
        version.DeletedAt = null;

        var fileRecord = await _dbContext.FileRecords.FindAsync(new object[] { version.FileRecordId }, cancellationToken);
        if (fileRecord != null)
        {
            fileRecord.IsDeleted = false;
            fileRecord.DeletedAt = null;
            fileRecord.VersionCount = await _dbContext.FileVersions
                .CountAsync(v => v.FileRecordId == fileRecord.Id && !v.IsDeleted, cancellationToken);
            fileRecord.UpdatedAt = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Version undeleted: {VersionId}", versionId);
        return true;
    }

    public async Task<bool> PermanentlyDeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.FileVersions.FindAsync(new object[] { versionId }, cancellationToken);
        if (version == null) return false;

        // 删除存储中的文件
        await _storageProvider.DeleteAsync(version.ObjectName, cancellationToken);

        // 删除本地缓存
        if (!string.IsNullOrEmpty(version.LocalCachePath) && File.Exists(version.LocalCachePath))
        {
            File.Delete(version.LocalCachePath);
        }

        // 删除数据库记录
        _dbContext.FileVersions.Remove(version);

        // 更新文件记录
        var fileRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == version.FileRecordId, cancellationToken);

        if (fileRecord != null)
        {
            fileRecord.VersionCount = fileRecord.Versions.Count(v => v.Id != versionId && !v.IsDeleted);
            
            if (fileRecord.CurrentVersionId == versionId)
            {
                var newLatest = fileRecord.Versions
                    .Where(v => v.Id != versionId && !v.IsDeleted)
                    .OrderByDescending(v => v.VersionNumber)
                    .FirstOrDefault();
                
                fileRecord.CurrentVersionId = newLatest?.Id;
            }

            // 如果没有版本了，删除文件记录
            if (!fileRecord.Versions.Any(v => v.Id != versionId))
            {
                _dbContext.FileRecords.Remove(fileRecord);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Version permanently deleted: {VersionId}", versionId);
        return true;
    }

    public async Task<bool> PermanentlyDeleteFileAsync(Guid fileRecordId, CancellationToken cancellationToken = default)
    {
        var fileRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Id == fileRecordId, cancellationToken);

        if (fileRecord == null) return false;

        // 删除所有版本
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

        _logger.LogInformation("File and all versions permanently deleted: {FileId}", fileRecordId);
        return true;
    }

    public async Task<int> CleanupOldVersionsAsync(Guid fileRecordId, int keepVersions, CancellationToken cancellationToken = default)
    {
        var versions = await _dbContext.FileVersions
            .Where(v => v.FileRecordId == fileRecordId && !v.IsDeleted && !v.IsLatest)
            .OrderByDescending(v => v.VersionNumber)
            .Skip(keepVersions)
            .ToListAsync(cancellationToken);

        int deletedCount = 0;
        foreach (var version in versions)
        {
            if (await PermanentlyDeleteVersionAsync(version.Id, cancellationToken))
            {
                deletedCount++;
            }
        }

        _logger.LogInformation("Cleaned up {Count} old versions for file: {FileId}", deletedCount, fileRecordId);
        return deletedCount;
    }

    public async Task<bool> CompareVersionsAsync(Guid versionId1, Guid versionId2, CancellationToken cancellationToken = default)
    {
        var v1 = await _dbContext.FileVersions.FindAsync(new object[] { versionId1 }, cancellationToken);
        var v2 = await _dbContext.FileVersions.FindAsync(new object[] { versionId2 }, cancellationToken);

        if (v1 == null || v2 == null) return false;

        return v1.FileHash.Equals(v2.FileHash, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<VersionStatistics> GetStatisticsAsync(Guid fileRecordId, CancellationToken cancellationToken = default)
    {
        var versions = await _dbContext.FileVersions
            .Where(v => v.FileRecordId == fileRecordId)
            .ToListAsync(cancellationToken);

        return new VersionStatistics
        {
            TotalVersions = versions.Count,
            ActiveVersions = versions.Count(v => !v.IsDeleted),
            DeletedVersions = versions.Count(v => v.IsDeleted),
            TotalSize = versions.Where(v => !v.IsDeleted).Sum(v => v.Size),
            FirstVersionDate = versions.Min(v => v.CreatedAt),
            LatestVersionDate = versions.Max(v => v.CreatedAt)
        };
    }

    #region 私有辅助方法

    private async Task<string> CacheVersionAsync(FileVersion version, Stream data, CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(_cacheDirectory, version.FileHash);
        
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        using (var fileStream = File.Create(cachePath))
        {
            await data.CopyToAsync(fileStream, cancellationToken);
        }

        return cachePath;
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
