namespace MinIOStorageService.Models;

/// <summary>
/// 存储配置设置
/// </summary>
public class StorageSettings
{
    /// <summary>
    /// 存储提供者类型：MinIO 或 Local
    /// </summary>
    public string Provider { get; set; } = "MinIO";

    /// <summary>
    /// 本地存储路径（当 Provider 为 Local 时使用）
    /// </summary>
    public string LocalPath { get; set; } = "Storage";

    /// <summary>
    /// MinIO 配置（当 Provider 为 MinIO 时使用）
    /// </summary>
    public MinIOSettings MinIO { get; set; } = new();
}
