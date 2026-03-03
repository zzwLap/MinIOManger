using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Services;

public interface IFileCacheService
{
    /// <summary>
    /// 上传文件并记录到数据库
    /// </summary>
    Task<FileRecord> UploadAndRecordAsync(IFormFile file, string? folder = null, string? description = null, string? tags = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件（优先从本地缓存，不一致则从MinIO下载并更新缓存）
    /// </summary>
    Task<(Stream Stream, FileRecord Record)> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 批量下载文件（打包为ZIP）
    /// </summary>
    Task<(Stream Stream, string FileName)> BatchDownloadAsync(List<Guid> fileIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 同步本地缓存与远程文件（检查一致性）
    /// </summary>
    Task<bool> SyncFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 强制刷新本地缓存
    /// </summary>
    Task<bool> RefreshCacheAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件记录列表
    /// </summary>
    Task<List<FileRecord>> GetFileRecordsAsync(string? search = null, string? tags = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 删除文件（数据库+MinIO+本地缓存）
    /// </summary>
    Task<bool> DeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清理过期的本地缓存
    /// </summary>
    Task<int> CleanExpiredCacheAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}
