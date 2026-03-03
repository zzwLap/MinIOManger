using Microsoft.AspNetCore.Mvc;
using MinIOStorageService.Services;

namespace MinIOStorageService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FoldersController : ControllerBase
{
    private readonly IMinioService _minioService;
    private readonly ILogger<FoldersController> _logger;

    public FoldersController(IMinioService minioService, ILogger<FoldersController> logger)
    {
        _minioService = minioService;
        _logger = logger;
    }

    /// <summary>
    /// 创建文件夹（通过创建占位对象实现）
    /// 注意：MinIO 是对象存储，实际上会创建一个以 / 结尾的空对象作为文件夹占位符
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateFolder([FromQuery] string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return BadRequest(new { message = "文件夹路径不能为空" });
        }

        var success = await _minioService.CreateFolderAsync(folderPath, cancellationToken);

        if (success)
        {
            return Ok(new 
            { 
                message = "文件夹创建成功（创建占位对象）", 
                folderPath,
                note = "MinIO 是对象存储，空文件夹通过占位对象实现"
            });
        }

        return BadRequest(new { message = "文件夹创建失败", folderPath });
    }

    /// <summary>
    /// 删除文件夹及其所有内容
    /// </summary>
    [HttpDelete("{folderPath}")]
    public async Task<IActionResult> DeleteFolder(string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return BadRequest(new { message = "文件夹路径不能为空" });
        }

        var success = await _minioService.DeleteFolderAsync(folderPath, cancellationToken);

        if (success)
        {
            return Ok(new { message = "文件夹删除成功", folderPath });
        }

        return BadRequest(new { message = "文件夹删除失败", folderPath });
    }

    /// <summary>
    /// 获取文件夹列表
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> ListFolders([FromQuery] string? parentFolder = null, CancellationToken cancellationToken = default)
    {
        var folders = await _minioService.ListFoldersAsync(parentFolder, cancellationToken);
        return Ok(new
        {
            parentFolder,
            count = folders.Count,
            folders
        });
    }

    /// <summary>
    /// 获取文件夹下的文件列表（仅当前层级）
    /// </summary>
    [HttpGet("files")]
    public async Task<IActionResult> ListFilesInFolder([FromQuery] string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return BadRequest(new { message = "文件夹路径不能为空" });
        }

        var files = await _minioService.ListFilesInFolderAsync(folderPath, cancellationToken);
        return Ok(new
        {
            folderPath,
            count = files.Count,
            files
        });
    }

    /// <summary>
    /// 递归获取文件夹下的所有文件（包含子文件夹）
    /// </summary>
    [HttpGet("files-recursive")]
    public async Task<IActionResult> ListFilesRecursive([FromQuery] string? folderPath = null, CancellationToken cancellationToken = default)
    {
        var files = await _minioService.ListFilesRecursiveAsync(folderPath, cancellationToken);
        return Ok(new
        {
            folderPath = folderPath ?? "root",
            count = files.Count,
            files
        });
    }

    /// <summary>
    /// 移动文件
    /// </summary>
    [HttpPost("move-file")]
    public async Task<IActionResult> MoveFile([FromQuery] string sourcePath, [FromQuery] string destinationPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
        {
            return BadRequest(new { message = "源路径和目标路径都不能为空" });
        }

        var success = await _minioService.MoveFileAsync(sourcePath, destinationPath, cancellationToken);

        if (success)
        {
            return Ok(new { message = "文件移动成功", sourcePath, destinationPath });
        }

        return BadRequest(new { message = "文件移动失败", sourcePath, destinationPath });
    }
}
