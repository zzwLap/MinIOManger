namespace MinIOStorageService.Models;

public class FileMetadata
{
    public string ObjectName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string ETag { get; set; } = string.Empty;
}

public class UploadResult
{
    public bool Success { get; set; }
    public string ObjectName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class BatchDownloadRequest
{
    public List<string> ObjectNames { get; set; } = new();
    public string? ZipFileName { get; set; }
}
