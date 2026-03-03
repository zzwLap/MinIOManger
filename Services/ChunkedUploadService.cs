using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data;
using MinIOStorageService.Data.Entities;
using System.Security.Cryptography;
using System.Text.Json;

namespace MinIOStorageService.Services;

public class ChunkedUploadService : IChunkedUploadService
{
    private readonly FileDbContext _dbContext;
    private readonly IStorageProvider _storageProvider;
    private readonly string _tempDirectory;
    private readonly ILogger<ChunkedUploadService> _logger;
    private const int DefaultChunkSize = 5 * 1024 * 1024; // 5MB

    public ChunkedUploadService(
        FileDbContext dbContext,
        IStorageProvider storageProvider,
        ILogger<ChunkedUploadService> logger)
    {
        _dbContext = dbContext;
        _storageProvider = storageProvider;
        _logger = logger;
        _tempDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempUploads");

        if (!Directory.Exists(_tempDirectory))
        {
            Directory.CreateDirectory(_tempDirectory);
        }
    }

    public async Task<UploadSession> InitiateUploadAsync(
        string fileName,
        long fileSize,
        string? fileHash = null,
        string? contentType = null,
        string? folder = null,
        string? description = null,
        string? tags = null,
        int? chunkSize = null,
        CancellationToken cancellationToken = default)
    {
        // 尝试秒传
        if (!string.IsNullOrEmpty(fileHash))
        {
            var quickResult = await TryQuickUploadAsync(fileHash, fileName, cancellationToken);
            if (quickResult?.Success == true && quickResult.FileVersion != null)
            {
                // 秒传成功，创建一个已完成的会话
                var quickSession = new UploadSession
                {
                    FileName = fileName,
                    FileSize = fileSize,
                    FileHash = fileHash,
                    ContentType = contentType ?? "application/octet-stream",
                    Folder = folder,
                    Description = description,
                    Tags = tags,
                    ChunkSize = chunkSize ?? DefaultChunkSize,
                    TotalChunks = 0,
                    UploadedChunks = "[]",
                    TempPath = string.Empty,
                    Status = UploadStatus.Completed,
                    CompletedAt = DateTime.UtcNow,
                    FileRecordId = quickResult.FileRecordId,
                    FileVersionId = quickResult.FileVersion.Id
                };

                _dbContext.UploadSessions.Add(quickSession);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Quick upload successful for file: {FileName}, Hash: {FileHash}", fileName, fileHash);
                return quickSession;
            }
        }

        // 正常创建上传会话
        var actualChunkSize = chunkSize ?? DefaultChunkSize;
        var totalChunks = (int)Math.Ceiling((double)fileSize / actualChunkSize);

        var session = new UploadSession
        {
            FileName = fileName,
            FileSize = fileSize,
            FileHash = fileHash,
            ContentType = contentType ?? "application/octet-stream",
            Folder = folder,
            Description = description,
            Tags = tags,
            ChunkSize = actualChunkSize,
            TotalChunks = totalChunks,
            UploadedChunks = "[]",
            TempPath = Path.Combine(_tempDirectory, Guid.NewGuid().ToString()),
            Status = UploadStatus.Pending
        };

        // 创建临时目录
        Directory.CreateDirectory(session.TempPath);

        _dbContext.UploadSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Upload session created: {UploadId}, File: {FileName}, Chunks: {TotalChunks}", 
            session.Id, fileName, totalChunks);

        return session;
    }

    public async Task<string> UploadChunkAsync(
        Guid uploadId,
        int chunkNumber,
        Stream chunkData,
        string? chunkHash = null,
        CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.UploadSessions.FindAsync(new object[] { uploadId }, cancellationToken);
        if (session == null)
        {
            throw new FileNotFoundException($"Upload session not found: {uploadId}");
        }

        if (session.Status == UploadStatus.Completed)
        {
            throw new InvalidOperationException("Upload already completed");
        }

        if (session.Status == UploadStatus.Cancelled)
        {
            throw new InvalidOperationException("Upload has been cancelled");
        }

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            session.Status = UploadStatus.Expired;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("Upload session has expired");
        }

        if (chunkNumber < 0 || chunkNumber >= session.TotalChunks)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkNumber), $"Chunk number must be between 0 and {session.TotalChunks - 1}");
        }

        // 检查分片是否已上传
        var uploadedChunks = JsonSerializer.Deserialize<List<int>>(session.UploadedChunks) ?? new List<int>();
        if (uploadedChunks.Contains(chunkNumber))
        {
            _logger.LogDebug("Chunk {ChunkNumber} already uploaded for session {UploadId}", chunkNumber, uploadId);
            return $"etag-{chunkNumber}";
        }

        // 保存分片到临时文件
        var chunkPath = Path.Combine(session.TempPath, $"chunk-{chunkNumber}");
        
        // 计算分片哈希
        string actualChunkHash;
        using (var ms = new MemoryStream())
        {
            await chunkData.CopyToAsync(ms, cancellationToken);
            ms.Position = 0;

            using (var md5 = MD5.Create())
            {
                var hash = await md5.ComputeHashAsync(ms, cancellationToken);
                actualChunkHash = Convert.ToHexString(hash);
            }

            // 验证分片哈希（如果提供）
            if (!string.IsNullOrEmpty(chunkHash) && !actualChunkHash.Equals(chunkHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Chunk hash mismatch. Expected: {chunkHash}, Actual: {actualChunkHash}");
            }

            // 保存到文件
            ms.Position = 0;
            await using (var fileStream = File.Create(chunkPath))
            {
                await ms.CopyToAsync(fileStream, cancellationToken);
            }
        }

        // 更新已上传分片列表
        uploadedChunks.Add(chunkNumber);
        uploadedChunks.Sort();
        session.UploadedChunks = JsonSerializer.Serialize(uploadedChunks);
        session.Status = UploadStatus.Uploading;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Chunk {ChunkNumber} uploaded for session {UploadId}, Hash: {Hash}", 
            chunkNumber, uploadId, actualChunkHash);

        return $"etag-{chunkNumber}-{actualChunkHash}";
    }

    public async Task<UploadStatusInfo> GetUploadStatusAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.UploadSessions.FindAsync(new object[] { uploadId }, cancellationToken);
        if (session == null)
        {
            throw new FileNotFoundException($"Upload session not found: {uploadId}");
        }

        var uploadedChunks = JsonSerializer.Deserialize<List<int>>(session.UploadedChunks) ?? new List<int>();
        var allChunkNumbers = Enumerable.Range(0, session.TotalChunks).ToList();
        var missingChunks = allChunkNumbers.Except(uploadedChunks).ToList();

        return new UploadStatusInfo
        {
            UploadId = session.Id,
            FileName = session.FileName,
            FileSize = session.FileSize,
            TotalChunks = session.TotalChunks,
            UploadedChunkCount = uploadedChunks.Count,
            UploadedChunks = uploadedChunks,
            MissingChunks = missingChunks,
            Status = session.Status,
            ExpiresAt = session.ExpiresAt
        };
    }

    public async Task<FileVersion> CompleteUploadAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.UploadSessions.FindAsync(new object[] { uploadId }, cancellationToken);
        if (session == null)
        {
            throw new FileNotFoundException($"Upload session not found: {uploadId}");
        }

        if (session.Status == UploadStatus.Completed)
        {
            // 已经完成的会话，返回关联的版本
            if (session.FileVersionId.HasValue)
            {
                var existingVersion = await _dbContext.FileVersions.FindAsync(new object[] { session.FileVersionId.Value }, cancellationToken);
                if (existingVersion != null) return existingVersion;
            }
            throw new InvalidOperationException("Upload already completed but version not found");
        }

        var uploadedChunks = JsonSerializer.Deserialize<List<int>>(session.UploadedChunks) ?? new List<int>();
        if (uploadedChunks.Count != session.TotalChunks)
        {
            throw new InvalidOperationException($"Not all chunks uploaded. Uploaded: {uploadedChunks.Count}, Total: {session.TotalChunks}");
        }

        session.Status = UploadStatus.Merging;
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            // 合并分片
            var mergedFilePath = Path.Combine(_tempDirectory, $"merged-{uploadId}");
            await MergeChunksAsync(session, mergedFilePath, cancellationToken);

            // 计算合并后文件的哈希
            string fileHash;
            using (var sha256 = SHA256.Create())
            await using (var stream = File.OpenRead(mergedFilePath))
            {
                var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
                fileHash = Convert.ToHexString(hash);
            }

            // 上传到存储
            var objectName = GenerateObjectName(session);
            await using (var stream = File.OpenRead(mergedFilePath))
            {
                await _storageProvider.UploadAsync(objectName, stream, session.ContentType, cancellationToken);
            }

            // 创建文件记录和版本
            var (fileRecord, fileVersion) = await CreateFileRecordAndVersionAsync(session, objectName, fileHash, cancellationToken);

            // 更新会话状态
            session.Status = UploadStatus.Completed;
            session.CompletedAt = DateTime.UtcNow;
            session.FileRecordId = fileRecord.Id;
            session.FileVersionId = fileVersion.Id;
            await _dbContext.SaveChangesAsync(cancellationToken);

            // 清理临时文件
            CleanupTempFiles(session);

            _logger.LogInformation("Upload completed: {UploadId}, File: {FileName}, Version: {VersionId}", 
                uploadId, session.FileName, fileVersion.Id);

            return fileVersion;
        }
        catch
        {
            session.Status = UploadStatus.Uploading;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> CancelUploadAsync(Guid uploadId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.UploadSessions.FindAsync(new object[] { uploadId }, cancellationToken);
        if (session == null) return false;

        if (session.Status == UploadStatus.Completed)
        {
            throw new InvalidOperationException("Cannot cancel completed upload");
        }

        session.Status = UploadStatus.Cancelled;
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 清理临时文件
        CleanupTempFiles(session);

        _logger.LogInformation("Upload cancelled: {UploadId}", uploadId);
        return true;
    }

    public async Task<int> CleanupExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var expiredSessions = await _dbContext.UploadSessions
            .Where(s => s.ExpiresAt < DateTime.UtcNow && s.Status != UploadStatus.Expired && s.Status != UploadStatus.Completed)
            .ToListAsync(cancellationToken);

        int count = 0;
        foreach (var session in expiredSessions)
        {
            session.Status = UploadStatus.Expired;
            CleanupTempFiles(session);
            count++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Cleaned up {Count} expired upload sessions", count);
        return count;
    }

    public async Task<QuickUploadResult?> TryQuickUploadAsync(string fileHash, string fileName, CancellationToken cancellationToken = default)
    {
        // 查找是否存在相同哈希的文件版本
        var existingVersion = await _dbContext.FileVersions
            .Include(v => v.FileRecord)
            .FirstOrDefaultAsync(v => v.FileHash == fileHash && !v.IsDeleted, cancellationToken);

        if (existingVersion == null)
        {
            return null;
        }

        // 创建新版本记录（指向相同存储对象）
        var fileRecord = existingVersion.FileRecord;
        if (fileRecord == null)
        {
            return null;
        }

        // 重置旧版本的 IsLatest
        foreach (var v in fileRecord.Versions.Where(v => v.IsLatest))
        {
            v.IsLatest = false;
        }

        var newVersionNumber = fileRecord.Versions.Where(v => !v.IsDeleted).Max(v => v.VersionNumber) + 1;

        // 创建新版本（引用相同的 ObjectName）
        var newVersion = new FileVersion
        {
            FileRecordId = fileRecord.Id,
            VersionNumber = newVersionNumber,
            ObjectName = existingVersion.ObjectName, // 引用相同的存储对象
            FileHash = existingVersion.FileHash,
            Size = existingVersion.Size,
            ChangeDescription = $"秒传（基于版本 {existingVersion.VersionNumber}）",
            IsLatest = true
        };

        _dbContext.FileVersions.Add(newVersion);

        // 更新文件记录
        fileRecord.CurrentVersionId = newVersion.Id;
        fileRecord.VersionCount = fileRecord.Versions.Count(v => !v.IsDeleted) + 1;
        fileRecord.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new QuickUploadResult
        {
            Success = true,
            Message = "秒传成功",
            FileVersion = newVersion,
            FileRecordId = fileRecord.Id
        };
    }

    #region 私有辅助方法

    private async Task MergeChunksAsync(UploadSession session, string outputPath, CancellationToken cancellationToken)
    {
        var uploadedChunks = JsonSerializer.Deserialize<List<int>>(session.UploadedChunks) ?? new List<int>();
        uploadedChunks.Sort();

        await using (var outputStream = File.Create(outputPath))
        {
            foreach (var chunkNumber in uploadedChunks)
            {
                var chunkPath = Path.Combine(session.TempPath, $"chunk-{chunkNumber}");
                if (!File.Exists(chunkPath))
                {
                    throw new FileNotFoundException($"Chunk file not found: {chunkPath}");
                }

                await using (var chunkStream = File.OpenRead(chunkPath))
                {
                    await chunkStream.CopyToAsync(outputStream, cancellationToken);
                }
            }
        }
    }

    private async Task<(FileRecord FileRecord, FileVersion FileVersion)> CreateFileRecordAndVersionAsync(
        UploadSession session, string objectName, string fileHash, CancellationToken cancellationToken)
    {
        // 查找是否已存在同名文件
        var existingRecord = await _dbContext.FileRecords
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.FileName == session.FileName && !f.IsDeleted, cancellationToken);

        FileRecord fileRecord;
        int newVersionNumber = 1;

        if (existingRecord != null)
        {
            fileRecord = existingRecord;
            newVersionNumber = fileRecord.Versions.Where(v => !v.IsDeleted).Max(v => v.VersionNumber) + 1;

            foreach (var v in fileRecord.Versions.Where(v => v.IsLatest))
            {
                v.IsLatest = false;
            }

            fileRecord.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(session.Description)) fileRecord.Description = session.Description;
            if (!string.IsNullOrEmpty(session.Tags)) fileRecord.Tags = session.Tags;
        }
        else
        {
            fileRecord = new FileRecord
            {
                FileName = session.FileName,
                ContentType = session.ContentType,
                Description = session.Description,
                Tags = session.Tags
            };
            _dbContext.FileRecords.Add(fileRecord);
        }

        // 获取文件大小
        var fileInfo = new FileInfo(Path.Combine(_tempDirectory, $"merged-{session.Id}"));

        var fileVersion = new FileVersion
        {
            FileRecordId = fileRecord.Id,
            VersionNumber = newVersionNumber,
            ObjectName = objectName,
            FileHash = fileHash,
            Size = fileInfo.Length,
            ChangeDescription = $"分片上传（{session.TotalChunks}个分片）",
            IsLatest = true
        };

        _dbContext.FileVersions.Add(fileVersion);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 更新文件记录
        fileRecord.CurrentVersionId = fileVersion.Id;
        fileRecord.VersionCount = fileRecord.Versions.Count(v => !v.IsDeleted);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return (fileRecord, fileVersion);
    }

    private string GenerateObjectName(UploadSession session)
    {
        var fileName = $"{Guid.NewGuid()}_{session.FileName}";
        return string.IsNullOrEmpty(session.Folder)
            ? fileName
            : $"{session.Folder.TrimEnd('/')}/{fileName}";
    }

    private void CleanupTempFiles(UploadSession session)
    {
        try
        {
            if (Directory.Exists(session.TempPath))
            {
                Directory.Delete(session.TempPath, true);
            }

            var mergedFile = Path.Combine(_tempDirectory, $"merged-{session.Id}");
            if (File.Exists(mergedFile))
            {
                File.Delete(mergedFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup temp files for upload session: {UploadId}", session.Id);
        }
    }

    #endregion
}
