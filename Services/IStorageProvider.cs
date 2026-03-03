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
    /// 获取提供者名称
    /// </summary>
    string ProviderName { get; }
}
