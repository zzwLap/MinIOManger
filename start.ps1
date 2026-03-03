# MinIO 文件存储服务启动脚本

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  MinIO 文件存储服务启动脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 检查 Docker 是否运行
try {
    $dockerInfo = docker info 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "错误: Docker 未运行，请先启动 Docker Desktop" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "错误: 无法连接到 Docker，请确保 Docker Desktop 已安装并运行" -ForegroundColor Red
    exit 1
}

Write-Host "✓ Docker 运行正常" -ForegroundColor Green

# 启动 MinIO
try {
    Write-Host ""
    Write-Host "正在启动 MinIO 服务..." -ForegroundColor Yellow
    docker-compose up -d
    
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Compose 启动失败"
    }
    
    Write-Host "✓ MinIO 服务已启动" -ForegroundColor Green
    Write-Host ""
    Write-Host "MinIO 访问信息:" -ForegroundColor Cyan
    Write-Host "  - API 端点: http://localhost:9000" -ForegroundColor White
    Write-Host "  - 管理控制台: http://localhost:9001" -ForegroundColor White
    Write-Host "  - Access Key: minioadmin" -ForegroundColor White
    Write-Host "  - Secret Key: minioadmin" -ForegroundColor White
    Write-Host ""
    
    # 等待 MinIO 完全启动
    Write-Host "等待 MinIO 服务就绪..." -ForegroundColor Yellow
    Start-Sleep -Seconds 5
    
} catch {
    Write-Host "错误: 启动 MinIO 失败 - $_" -ForegroundColor Red
    exit 1
}

# 启动 ASP.NET Core 服务
try {
    Write-Host "正在启动 ASP.NET Core 服务..." -ForegroundColor Yellow
    Write-Host ""
    
    # 检查 dotnet 是否安装
    $dotnetVersion = dotnet --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "错误: .NET SDK 未安装，请先安装 .NET SDK" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "✓ .NET SDK 版本: $dotnetVersion" -ForegroundColor Green
    Write-Host ""
    Write-Host "正在构建项目..." -ForegroundColor Yellow
    
    dotnet build MinIOStorageService.csproj
    
    if ($LASTEXITCODE -ne 0) {
        throw "项目构建失败"
    }
    
    Write-Host "✓ 项目构建成功" -ForegroundColor Green
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  服务启动完成!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "API 服务地址:" -ForegroundColor Cyan
    Write-Host "  - API: https://localhost:7001" -ForegroundColor White
    Write-Host "  - API: http://localhost:5001" -ForegroundColor White
    Write-Host "  - Swagger UI: https://localhost:7001/swagger" -ForegroundColor White
    Write-Host ""
    Write-Host "MinIO 服务地址:" -ForegroundColor Cyan
    Write-Host "  - API: http://localhost:9000" -ForegroundColor White
    Write-Host "  - 控制台: http://localhost:9001" -ForegroundColor White
    Write-Host ""
    Write-Host "按 Ctrl+C 停止服务" -ForegroundColor Yellow
    Write-Host ""
    
    # 运行服务
    dotnet run --project MinIOStorageService.csproj --urls "https://localhost:7001;http://localhost:5001"
    
} catch {
    Write-Host "错误: 启动 ASP.NET Core 服务失败 - $_" -ForegroundColor Red
    exit 1
}
