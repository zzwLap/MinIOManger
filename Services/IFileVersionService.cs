using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Services;

/// <summary>
/// 文件版本管理服务接口
/// </summary>
public interface IFileVersionService
{
    /// <summary>
    /// 获取文件的所有版本列表
    /// </summary>
    Task<List<FileVersion>> GetVersionsAsync(Guid fileRecordId, bool includeDeleted = false, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取指定版本信息
    /// </summary>
    Task<FileVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 下载指定版本文件
    /// </summary>
    Task<(Stream Stream, FileVersion Version)> DownloadVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 恢复到指定版本（创建新版本指向旧版本内容）
    /// </summary>
    Task<FileVersion?> RestoreVersionAsync(Guid versionId, string? changeDescription = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 软删除指定版本
    /// </summary>
    Task<bool> SoftDeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 恢复软删除的版本
    /// </summary>
    Task<bool> UndeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 彻底删除指定版本（物理删除）
    /// </summary>
    Task<bool> PermanentlyDeleteVersionAsync(Guid versionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 彻底删除文件的所有版本
    /// </summary>
    Task<bool> PermanentlyDeleteFileAsync(Guid fileRecordId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 清理旧版本（保留最近N个版本）
    /// </summary>
    Task<int> CleanupOldVersionsAsync(Guid fileRecordId, int keepVersions, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 比较两个版本的差异（基于哈希）
    /// </summary>
    Task<bool> CompareVersionsAsync(Guid versionId1, Guid versionId2, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取版本统计信息
    /// </summary>
    Task<VersionStatistics> GetStatisticsAsync(Guid fileRecordId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 版本统计信息
/// </summary>
public class VersionStatistics
{
    public int TotalVersions { get; set; }
    public int ActiveVersions { get; set; }
    public int DeletedVersions { get; set; }
    public long TotalSize { get; set; }
    public DateTime? FirstVersionDate { get; set; }
    public DateTime? LatestVersionDate { get; set; }
}
