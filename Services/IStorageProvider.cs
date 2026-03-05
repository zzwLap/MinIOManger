namespace MinIOStorageService.Services;

/// <summary>
/// 存储提供者接口 - 支持 MinIO 和本地文件系统两种实现
/// </summary>
public interface IStorageProvider
{
    /// <summary>
    /// 上传文件
    /// </summary>
    Task<string> UploadAsync(string objectName, Stream data, string contentType, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 下载文件到流
    /// </summary>
    Task DownloadAsync(string objectName, Stream destination, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 删除文件
    /// </summary>
    Task<bool> DeleteAsync(string objectName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 检查文件是否存在
    /// </summary>
    Task<bool> ExistsAsync(string objectName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件流
    /// </summary>
    Task<Stream> GetStreamAsync(string objectName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件指定范围的流（支持断点续传）
    /// </summary>
    /// <param name="objectName">对象名称</param>
    /// <param name="start">起始位置（字节）</param>
    /// <param name="end">结束位置（字节），null表示到文件末尾</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>指定范围的数据流</returns>
    Task<Stream> GetRangeStreamAsync(string objectName, long start, long? end = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取文件大小
    /// </summary>
    Task<long> GetFileSizeAsync(string objectName, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 生成预签名下载URL（支持Range下载）
    /// </summary>
    /// <param name="objectName">对象名称</param>
    /// <param name="expiration">过期时间</param>
    /// <param name="start">起始位置（可选）</param>
    /// <param name="end">结束位置（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>预签名URL</returns>
    Task<string> GetPresignedDownloadUrlAsync(string objectName, TimeSpan expiration, long? start = null, long? end = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取提供者名称
    /// </summary>
    string ProviderName { get; }
    
    /// <summary>
    /// 获取对象元数据（ETag、最后修改时间等）
    /// </summary>
    /// <param name="objectName">对象名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>对象元数据</returns>
    Task<ObjectMetadata> GetObjectMetadataAsync(string objectName, CancellationToken cancellationToken = default);
}

/// <summary>
/// 存储对象元数据
/// </summary>
public class ObjectMetadata
{
    /// <summary>
    /// 对象名称
    /// </summary>
    public string ObjectName { get; set; } = string.Empty;
    
    /// <summary>
    /// ETag（通常是对象内容的哈希值）
    /// </summary>
    public string ETag { get; set; } = string.Empty;
    
    /// <summary>
    /// 最后修改时间
    /// </summary>
    public DateTime LastModified { get; set; }
    
    /// <summary>
    /// 对象大小（字节）
    /// </summary>
    public long Size { get; set; }
    
    /// <summary>
    /// 内容类型
    /// </summary>
    public string? ContentType { get; set; }
}
