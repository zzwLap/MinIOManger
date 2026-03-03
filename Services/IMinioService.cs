using MinIOStorageService.Models;

namespace MinIOStorageService.Services;

public interface IMinioService
{
    // 文件操作
    Task<UploadResult> UploadFileAsync(IFormFile file, string? folder = null, string? objectName = null, CancellationToken cancellationToken = default);
    Task<UploadResult> UploadFileWithPathAsync(IFormFile file, string fullPath, CancellationToken cancellationToken = default);
    Task<Stream> DownloadFileAsync(string objectName, CancellationToken cancellationToken = default);
    Task<FileMetadata?> GetFileMetadataAsync(string objectName, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string objectName, CancellationToken cancellationToken = default);
    Task<List<FileMetadata>> ListFilesAsync(string? prefix = null, CancellationToken cancellationToken = default);
    Task<bool> FileExistsAsync(string objectName, CancellationToken cancellationToken = default);
    Task<Stream> CreateBatchDownloadZipAsync(IEnumerable<string> objectNames, string zipFileName, CancellationToken cancellationToken = default);
    
    // 文件夹操作（虚拟文件夹，基于对象键前缀）
    Task<bool> CreateFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<bool> DeleteFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<List<string>> ListFoldersAsync(string? parentFolder = null, CancellationToken cancellationToken = default);
    Task<List<FileMetadata>> ListFilesInFolderAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<List<FileMetadata>> ListFilesRecursiveAsync(string? prefix = null, CancellationToken cancellationToken = default);
    Task<bool> MoveFileAsync(string sourceObjectName, string destinationObjectName, CancellationToken cancellationToken = default);
    
    // Bucket 操作
    Task<bool> CreateBucketAsync(string bucketName, CancellationToken cancellationToken = default);
    Task<bool> DeleteBucketAsync(string bucketName, CancellationToken cancellationToken = default);
    Task<bool> BucketExistsAsync(string bucketName, CancellationToken cancellationToken = default);
    Task<List<string>> ListBucketsAsync(CancellationToken cancellationToken = default);
    Task<bool> SetCurrentBucketAsync(string bucketName);
}
