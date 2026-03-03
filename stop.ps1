# MinIO 文件存储服务停止脚本

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  正在停止服务..." -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 停止 Docker Compose 服务
try {
    Write-Host "正在停止 MinIO 服务..." -ForegroundColor Yellow
    docker-compose down
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ MinIO 服务已停止" -ForegroundColor Green
    } else {
        Write-Host "警告: MinIO 服务停止时出现问题" -ForegroundColor Yellow
    }
} catch {
    Write-Host "警告: 停止 MinIO 服务时出错 - $_" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  服务已停止" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
