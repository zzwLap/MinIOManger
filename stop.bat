@echo off
chcp 65001 >nul
echo ========================================
echo   MinIO 文件存储服务停止脚本
echo ========================================
echo.

powershell -ExecutionPolicy Bypass -File "%~dp0stop.ps1"

pause
