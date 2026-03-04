using Minio;
using Minio.DataModel.Args;
using MinIOStorageService.Models;
using System.IO.Compression;
using System.Reactive.Linq;

namespace MinIOStorageService.Services;

public class MinioService : IMinioService
{
    private readonly IMinioClient _minioClient;
    private string _bucketName;
    private readonly ILogger<MinioService> _logger;

    public MinioService(IMinioClient minioClient, MinIOSettings settings, ILogger<MinioService> logger)
    {
        _minioClient = minioClient;
        _bucketName = settings.BucketName;
        _logger = logger;
    }

    public async Task<string> UploadFileAsync(IFormFile file, string? folder = null, string? objectName = null, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        var fileName = objectName ?? $"{Guid.NewGuid()}_{file.FileName}";
        
        // 构建完整路径（包含文件夹）
        var fullPath = string.IsNullOrEmpty(folder) 
            ? fileName 
            : $"{folder.TrimEnd('/')}/{fileName}";
        
        using var stream = file.OpenReadStream();
        
        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fullPath)
            .WithStreamData(stream)
            .WithObjectSize(file.Length)
            .WithContentType(file.ContentType);

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);

        _logger.LogInformation("File uploaded successfully: {FullPath}", fullPath);

        return fullPath;
    }

    public async Task<Stream> DownloadFileAsync(string objectName, CancellationToken cancellationToken = default)
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

    public async Task<FileMetadata?> GetFileMetadataAsync(string objectName, CancellationToken cancellationToken = default)
    {
        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName);

            var objectStat = await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);

            return new FileMetadata
            {
                ObjectName = objectName,
                FileName = Path.GetFileName(objectName),
                ContentType = objectStat.ContentType,
                Size = (long)objectStat.Size,
                LastModified = objectStat.LastModified,
                ETag = objectStat.ETag
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get metadata for object: {ObjectName}", objectName);
            return null;
        }
    }

    public async Task<bool> DeleteFileAsync(string objectName, CancellationToken cancellationToken = default)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(objectName);

        await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
        _logger.LogInformation("File deleted successfully: {ObjectName}", objectName);
        return true;
    }

    public async Task<List<FileMetadata>> ListFilesAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        var files = new List<FileMetadata>();
        
        var listObjectsArgs = new ListObjectsArgs()
            .WithBucket(_bucketName)
            .WithPrefix(prefix ?? "")
            .WithRecursive(true);
            
        await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs, cancellationToken))
        {
            if (!item.IsDir)
            {
                files.Add(new FileMetadata
                {
                    ObjectName = item.Key,
                    FileName = Path.GetFileName(item.Key),
                    Size = (long)item.Size,
                    LastModified = item.LastModifiedDateTime.GetValueOrDefault()
                });
            }
        }

        return files;
    }

    public async Task<bool> FileExistsAsync(string objectName, CancellationToken cancellationToken = default)
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

    public async Task<Stream> CreateBatchDownloadZipAsync(IEnumerable<string> objectNames, string zipFileName, CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        
        using (var zipArchive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            foreach (var objectName in objectNames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (!await FileExistsAsync(objectName, cancellationToken))
                {
                    _logger.LogWarning("File not found for batch download: {ObjectName}", objectName);
                    continue;
                }

                try
                {
                    var fileStream = await DownloadFileAsync(objectName, cancellationToken);
                    var entryName = Path.GetFileName(objectName);
                    
                    var entry = zipArchive.CreateEntry(entryName, CompressionLevel.Optimal);
                    await using (var entryStream = entry.Open())
                    {
                        await fileStream.CopyToAsync(entryStream, cancellationToken);
                    }
                    
                    _logger.LogDebug("Added to zip: {ObjectName}", objectName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to add file to zip: {ObjectName}", objectName);
                }
            }
        }

        memoryStream.Position = 0;
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

    #region 文件夹操作（虚拟文件夹，基于对象键前缀）

    /// <summary>
    /// 创建文件夹（通过上传占位文件实现）
    /// 注意：MinIO 是对象存储，空文件夹不会被保留，此方法创建一个以 / 结尾的空对象作为占位
    /// </summary>
    public async Task<bool> CreateFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        // 规范化路径：确保以 / 结尾
        var folderKey = folderPath.TrimEnd('/') + "/";
        
        // 创建占位对象（空内容）
        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(folderKey)
            .WithStreamData(new MemoryStream())
            .WithObjectSize(0)
            .WithContentType("application/x-directory");

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);
        _logger.LogInformation("Folder placeholder created: {FolderPath}", folderPath);
        return true;
    }

    public async Task<string> UploadFileWithPathAsync(IFormFile file, string fullPath, CancellationToken cancellationToken = default)
    {
        await EnsureBucketExistsAsync(cancellationToken);

        using var stream = file.OpenReadStream();
        
        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fullPath)
            .WithStreamData(stream)
            .WithObjectSize(file.Length)
            .WithContentType(file.ContentType);

        await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);

        _logger.LogInformation("File uploaded successfully: {FullPath}", fullPath);

        return fullPath;
    }

    public async Task<bool> DeleteFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        // 获取文件夹下的所有对象
        var prefix = folderPath.TrimEnd('/') + "/";
        var listObjectsArgs = new ListObjectsArgs()
            .WithBucket(_bucketName)
            .WithPrefix(prefix)
            .WithRecursive(true);

        var objectsToDelete = new List<string>();
        await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs, cancellationToken))
        {
            objectsToDelete.Add(item.Key);
        }

        // 批量删除对象
        foreach (var objKey in objectsToDelete)
        {
            var removeObjectArgs = new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objKey);
            await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);
        }

        _logger.LogInformation("Folder deleted successfully: {FolderPath}, deleted {Count} objects", folderPath, objectsToDelete.Count);
        return true;
    }

    public async Task<List<string>> ListFoldersAsync(string? parentFolder = null, CancellationToken cancellationToken = default)
    {
        var folders = new HashSet<string>();
        var prefix = parentFolder != null ? parentFolder.TrimEnd('/') + "/" : "";
        
        var listObjectsArgs = new ListObjectsArgs()
            .WithBucket(_bucketName)
            .WithPrefix(prefix)
            .WithRecursive(false);

        await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs, cancellationToken))
        {
            if (item.IsDir)
            {
                // 移除末尾的 /
                var folderName = item.Key.TrimEnd('/');
                if (!string.IsNullOrEmpty(folderName))
                {
                    folders.Add(folderName);
                }
            }
            else if (item.Key.Contains('/'))
            {
                // 从文件路径提取文件夹
                var relativePath = item.Key.Substring(prefix.Length);
                var slashIndex = relativePath.IndexOf('/');
                if (slashIndex > 0)
                {
                    var folder = prefix + relativePath.Substring(0, slashIndex);
                    folders.Add(folder);
                }
            }
        }

        return folders.ToList();
    }

    public async Task<List<FileMetadata>> ListFilesInFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var prefix = folderPath.TrimEnd('/') + "/";
        var files = new List<FileMetadata>();
        
        var listObjectsArgs = new ListObjectsArgs()
            .WithBucket(_bucketName)
            .WithPrefix(prefix)
            .WithRecursive(false);

        await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs, cancellationToken))
        {
            if (!item.IsDir && item.Key != prefix)
            {
                files.Add(new FileMetadata
                {
                    ObjectName = item.Key,
                    FileName = Path.GetFileName(item.Key),
                    Size = (long)item.Size,
                    LastModified = item.LastModifiedDateTime.GetValueOrDefault()
                });
            }
        }

        return files;
    }

    public async Task<List<FileMetadata>> ListFilesRecursiveAsync(string? prefix = null, CancellationToken cancellationToken = default)
    {
        var files = new List<FileMetadata>();
        
        var listObjectsArgs = new ListObjectsArgs()
            .WithBucket(_bucketName)
            .WithPrefix(prefix ?? "")
            .WithRecursive(true);

        await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs, cancellationToken))
        {
            // 跳过文件夹占位符（以 / 结尾且大小为0）
            if (!item.IsDir && !(item.Key.EndsWith("/") && item.Size == 0))
            {
                files.Add(new FileMetadata
                {
                    ObjectName = item.Key,
                    FileName = Path.GetFileName(item.Key),
                    Size = (long)item.Size,
                    LastModified = item.LastModifiedDateTime.GetValueOrDefault()
                });
            }
        }

        return files;
    }

    public async Task<bool> MoveFileAsync(string sourceObjectName, string destinationObjectName, CancellationToken cancellationToken = default)
    {
        // 复制对象
        var copySourceArgs = new CopySourceObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(sourceObjectName);

        var copyObjectArgs = new CopyObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(destinationObjectName)
            .WithCopyObjectSource(copySourceArgs);

        await _minioClient.CopyObjectAsync(copyObjectArgs, cancellationToken);

        // 删除原对象
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(sourceObjectName);
        await _minioClient.RemoveObjectAsync(removeObjectArgs, cancellationToken);

        _logger.LogInformation("File moved from {Source} to {Destination}", sourceObjectName, destinationObjectName);
        return true;
    }

    #endregion

    #region Bucket 操作

    public async Task<bool> CreateBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var makeBucketArgs = new MakeBucketArgs()
            .WithBucket(bucketName);
        await _minioClient.MakeBucketAsync(makeBucketArgs, cancellationToken);
        _logger.LogInformation("Bucket created: {BucketName}", bucketName);
        return true;
    }

    public async Task<bool> DeleteBucketAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var removeBucketArgs = new RemoveBucketArgs()
            .WithBucket(bucketName);
        await _minioClient.RemoveBucketAsync(removeBucketArgs, cancellationToken);
        _logger.LogInformation("Bucket deleted: {BucketName}", bucketName);
        return true;
    }

    public async Task<bool> BucketExistsAsync(string bucketName, CancellationToken cancellationToken = default)
    {
        var bucketExistsArgs = new BucketExistsArgs()
            .WithBucket(bucketName);
        return await _minioClient.BucketExistsAsync(bucketExistsArgs, cancellationToken);
    }

    public async Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken = default)
    {
        var bucketsResult = await _minioClient.ListBucketsAsync(cancellationToken);
        return bucketsResult.Buckets.Select(b => b.Name).ToList();
    }

    public Task<bool> SetCurrentBucketAsync(string bucketName)
    {
        _bucketName = bucketName;
        _logger.LogInformation("Current bucket set to: {BucketName}", bucketName);
        return Task.FromResult(true);
    }

    #endregion
}
