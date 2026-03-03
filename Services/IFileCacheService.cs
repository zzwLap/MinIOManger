using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Services;

public interface IFileCacheService
{
    /// <summary>
    /// 上传文件并创建新版本
    /// </summary>
    /// <param name="file">上传的文件</param>
    /// <param name="folder">文件夹路径</param>
    /// <param name="description">文件描述</param>
    /// <param name="tags">标签</param>
    /// <param name="changeDescription">版本变更说明</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>创建的文件版本记录</returns>
    Task<FileVersion> UploadAndRecordAsync(
        IFormFile file, 
        string? folder = null, 
        string? description = null, 
        string? tags = null,
        string? changeDescription = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件最新版本（优先从本地缓存，不一致则从存储下载并更新缓存）
    /// </summary>
    Task<(Stream Stream, FileVersion Version)> GetFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 批量下载文件最新版本（打包为ZIP）
    /// </summary>
    Task<(Stream Stream, string FileName)> BatchDownloadAsync(List<Guid> fileIds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 同步本地缓存与远程文件（检查一致性）
    /// </summary>
    Task<bool> SyncFileAsync(Guid versionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 强制刷新本地缓存
    /// </summary>
    Task<bool> RefreshCacheAsync(Guid versionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件记录列表（不含版本详情）
    /// </summary>
    Task<List<FileRecord>> GetFileRecordsAsync(string? search = null, string? tags = null, bool includeDeleted = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件记录详情（含版本列表）
    /// </summary>
    Task<FileRecord?> GetFileRecordAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 软删除文件（标记删除，可恢复）
    /// </summary>
    Task<bool> SoftDeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 恢复软删除的文件
    /// </summary>
    Task<bool> UndeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 彻底删除文件及所有版本
    /// </summary>
    Task<bool> PermanentlyDeleteFileAsync(Guid fileId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清理过期的本地缓存
    /// </summary>
    Task<int> CleanExpiredCacheAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}
