@echo off
chcp 65001 >nul
echo ========================================
echo   MinIO 文件存储服务启动脚本
echo ========================================
echo.

REM 检查 PowerShell 是否可用
where powershell >nul 2>&1
if %errorlevel% neq 0 (
    echo 错误: 未找到 PowerShell
    pause
    exit /b 1
)

REM 执行 PowerShell 脚本
powershell -ExecutionPolicy Bypass -File "%~dp0start.ps1"

pause
