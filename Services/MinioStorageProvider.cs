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
        try
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName);

            await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
            
            _logger.LogInformation("File deleted from MinIO: {ObjectName}", objectName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete file from MinIO: {ObjectName}", objectName);
            return false;
        }
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
        catch
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
