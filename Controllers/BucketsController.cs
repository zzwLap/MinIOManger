using Microsoft.AspNetCore.Mvc;
using MinIOStorageService.Services;

namespace MinIOStorageService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BucketsController : ControllerBase
{
    private readonly IMinioService _minioService;
    private readonly ILogger<BucketsController> _logger;

    public BucketsController(IMinioService minioService, ILogger<BucketsController> logger)
    {
        _minioService = minioService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有 Bucket 列表
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> ListBuckets(CancellationToken cancellationToken = default)
    {
        var buckets = await _minioService.ListBucketsAsync(cancellationToken);
        return Ok(new
        {
            count = buckets.Count,
            buckets
        });
    }

    /// <summary>
    /// 创建 Bucket
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateBucket([FromQuery] string bucketName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return BadRequest(new { message = "Bucket 名称不能为空" });
        }

        var success = await _minioService.CreateBucketAsync(bucketName, cancellationToken);

        if (success)
        {
            return Ok(new { message = "Bucket 创建成功", bucketName });
        }

        return BadRequest(new { message = "Bucket 创建失败", bucketName });
    }

    /// <summary>
    /// 删除 Bucket
    /// </summary>
    [HttpDelete("{bucketName}")]
    public async Task<IActionResult> DeleteBucket(string bucketName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return BadRequest(new { message = "Bucket 名称不能为空" });
        }

        var success = await _minioService.DeleteBucketAsync(bucketName, cancellationToken);

        if (success)
        {
            return Ok(new { message = "Bucket 删除成功", bucketName });
        }

        return BadRequest(new { message = "Bucket 删除失败", bucketName });
    }

    /// <summary>
    /// 检查 Bucket 是否存在
    /// </summary>
    [HttpGet("exists/{bucketName}")]
    public async Task<IActionResult> BucketExists(string bucketName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return BadRequest(new { message = "Bucket 名称不能为空" });
        }

        var exists = await _minioService.BucketExistsAsync(bucketName, cancellationToken);
        return Ok(new { bucketName, exists });
    }

    /// <summary>
    /// 切换当前使用的 Bucket
    /// </summary>
    [HttpPost("set-current")]
    public async Task<IActionResult> SetCurrentBucket([FromQuery] string bucketName)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return BadRequest(new { message = "Bucket 名称不能为空" });
        }

        var success = await _minioService.SetCurrentBucketAsync(bucketName);

        if (success)
        {
            return Ok(new { message = "当前 Bucket 已切换", bucketName });
        }

        return BadRequest(new { message = "切换 Bucket 失败", bucketName });
    }
}
