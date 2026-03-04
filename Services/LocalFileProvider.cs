namespace MinIOStorageService.Services;

/// <summary>
/// 本地文件系统存储提供者 - 用于没有 MinIO 的环境
/// </summary>
public class LocalFileProvider : IStorageProvider
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileProvider> _logger;

    public LocalFileProvider(string basePath, ILogger<LocalFileProvider> logger)
    {
        _basePath = basePath;
        _logger = logger;
        
        // 确保基础目录存在
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public string ProviderName => "LocalFileSystem";

    public async Task<string> UploadAsync(string objectName, Stream data, string contentType, CancellationToken cancellationToken = default)
    {
        var filePath = GetFullPath(objectName);
        var directory = Path.GetDirectoryName(filePath);
        
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var fileStream = File.Create(filePath);
        await data.CopyToAsync(fileStream, cancellationToken);
        
        _logger.LogInformation("File uploaded to local storage: {ObjectName}", objectName);
        return objectName;
    }

    public async Task DownloadAsync(string objectName, Stream destination, CancellationToken cancellationToken = default)
    {
        var filePath = GetFullPath(objectName);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {objectName}");
        }

        using var fileStream = File.OpenRead(filePath);
        await fileStream.CopyToAsync(destination, cancellationToken);
    }

    public Task<bool> DeleteAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var filePath = GetFullPath(objectName);
        
        if (!File.Exists(filePath))
        {
            return Task.FromResult(false);
        }

        File.Delete(filePath);
        
        // 尝试清理空目录
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            var dirInfo = new DirectoryInfo(directory);
            if (!dirInfo.EnumerateFileSystemInfos().Any())
            {
                Directory.Delete(directory);
            }
        }
        
        _logger.LogInformation("File deleted from local storage: {ObjectName}", objectName);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var filePath = GetFullPath(objectName);
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<Stream> GetStreamAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var filePath = GetFullPath(objectName);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {objectName}");
        }

        // 返回文件流，调用者负责关闭
        var stream = File.OpenRead(filePath);
        return Task.FromResult<Stream>(stream);
    }

    public Task<Stream> GetRangeStreamAsync(string objectName, long start, long? end = null, CancellationToken cancellationToken = default)
    {
        var filePath = GetFullPath(objectName);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {objectName}");
        }

        var fileInfo = new FileInfo(filePath);
        var fileLength = fileInfo.Length;

        // 验证范围
        if (start < 0 || start >= fileLength)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Start position is out of range");
        }

        var actualEnd = end ?? fileLength - 1;
        if (actualEnd >= fileLength)
        {
            actualEnd = fileLength - 1;
        }

        if (start > actualEnd)
        {
            throw new ArgumentException("Start position cannot be greater than end position");
        }

        var stream = File.OpenRead(filePath);
        stream.Seek(start, SeekOrigin.Begin);

        // 返回限制长度的流
        var rangeLength = actualEnd - start + 1;
        var limitedStream = new RangeStream(stream, rangeLength);
        
        _logger.LogDebug("Range stream created: {ObjectName}, bytes {Start}-{End}", objectName, start, actualEnd);
        return Task.FromResult<Stream>(limitedStream);
    }

    public Task<long> GetFileSizeAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var filePath = GetFullPath(objectName);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {objectName}");
        }

        var fileInfo = new FileInfo(filePath);
        return Task.FromResult(fileInfo.Length);
    }

    public Task<string> GetPresignedDownloadUrlAsync(string objectName, TimeSpan expiration, long? start = null, long? end = null, CancellationToken cancellationToken = default)
    {
        // 本地存储不支持预签名URL，返回本地文件路径
        // 客户端需要通过 API 代理下载
        var filePath = GetFullPath(objectName);
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {objectName}");
        }

        // 返回特殊格式的URL，表示需要通过API下载
        var rangeParam = start.HasValue ? $"&start={start}" : "";
        rangeParam += end.HasValue ? $"&end={end}" : "";
        
        var url = $"/api/download/local/{Uri.EscapeDataString(objectName)}?expires={DateTimeOffset.UtcNow.Add(expiration).ToUnixTimeSeconds()}{rangeParam}";
        return Task.FromResult(url);
    }

    private string GetFullPath(string objectName)
    {
        // 将对象名称中的 / 转换为路径分隔符
        var relativePath = objectName.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_basePath, relativePath);
    }
}
