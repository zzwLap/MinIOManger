using Minio;
using Minio.DataModel.Args;

namespace MinIOStorageService.Services;

/// <summary>
/// MinIO 存储提供者 - 包装 MinIO 客户端实现 IStorageProvider 接口
/// </summary>
public class MinioStorageProvider : IStorageProvider
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName;
    private readonly ILogger<MinioStorageProvider> _logger;

    public MinioStorageProvider(IMinioClient minioClient, string bucketName, ILogger<MinioStorageProvider> logger)
    {
        _minioClient = minioClient;
        _bucketName = bucketName;
        _logger = logger;
    }

    public string ProviderName => "MinIO";

    public async Task<string> UploadAsync(string objectName, Stream data, string contentType, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName)
            .WithStreamData(data)
            .WithObjectSize(data.Length)
            .WithContentType(contentType);

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
        
        _logger.LogInformation("File uploaded to MinIO: {ObjectName}", objectName);
        return objectName;
    }

    public async Task DownloadAsync(string objectName, Stream destination, CancellationToken cancellationToken = default)
    {
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName)
            .WithCallbackStream(stream =>
            {
                stream.CopyTo(destination);
            });

        await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName);

        await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
        
        _logger.LogInformation("File deleted from MinIO: {ObjectName}", objectName);
        return true;
    }

    public async Task<bool> ExistsAsync(string objectName, CancellationToken cancellationToken = default)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName);

            await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
            return true;
        }
        catch (Minio.Exceptions.ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task<Stream> GetStreamAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName)
            .WithCallbackStream(stream =>
            {
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;
            });

        await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
        return memoryStream;
    }

    public async Task<Stream> GetRangeStreamAsync(string objectName, long start, long? end = null, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        
        // MinIO SDK 7.0 支持 WithOffsetAndLength 实现 Range 读取
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName)
            .WithOffsetAndLength(start, end.HasValue ? end.Value - start + 1 : 0)
            .WithCallbackStream(stream =>
            {
                stream.CopyTo(memoryStream);
                memoryStream.Position = 0;
            });

        await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);
        return memoryStream;
    }

    public async Task<long> GetFileSizeAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var statObjectArgs = new StatObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName);

        var stat = await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
        return stat.Size;
    }

    public async Task<string> GetPresignedDownloadUrlAsync(string objectName, TimeSpan expiration, long? start = null, long? end = null, CancellationToken cancellationToken = default)
    {
        // 构建请求参数
        var presignedGetObjectArgs = new PresignedGetObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName)
            .WithExpiry((int)expiration.TotalSeconds);

        // 如果指定了范围，添加到请求
        if (start.HasValue || end.HasValue)
        {
            var range = start.HasValue && end.HasValue
                ? $"bytes={start.Value}-{end.Value}"
                : start.HasValue
                    ? $"bytes={start.Value}-"
                    : $"bytes=0-{end.Value}";
            
            // MinIO SDK 6.x 可能不支持直接在预签名URL中添加Range
            // 需要在客户端添加 Range 头部
        }

        var url = await _minioClient.PresignedGetObjectAsync(presignedGetObjectArgs);
        
        _logger.LogDebug("Generated presigned URL for {ObjectName}, expires in {Expiration}s", 
            objectName, expiration.TotalSeconds);
        
        return url;
    }

    public async Task<ObjectMetadata> GetObjectMetadataAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var statObjectArgs = new StatObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName);

        var stat = await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);
        
        // MinIO 的 ETag 通常包含引号，需要去除
        var etag = stat.ETag?.Trim('"') ?? string.Empty;
        
        // 将 UTC 时间转换为本地时间
        var lastModified = stat.LastModified;
        
        _logger.LogDebug("Retrieved metadata for {ObjectName}: ETag={ETag}, LastModified={LastModified}, Size={Size}",
            objectName, etag, lastModified, stat.Size);
        
        return new ObjectMetadata
        {
            ObjectName = objectName,
            ETag = etag,
            LastModified = lastModified,
            Size = stat.Size,
            ContentType = stat.ContentType
        };
    }

    private async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        var bucketExistsArgs = new BucketExistsArgs()
            .WithBucket(_bucketName);
        
        var exists = await _minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);
        
        if (!exists)
        {
            var makeBucketArgs = new MakeBucketArgs()
                .WithBucket(_bucketName);
            await _minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
            _logger.LogInformation("Created bucket: {BucketName}", _bucketName);
        }
    }
}
