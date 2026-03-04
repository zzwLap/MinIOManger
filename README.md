 MinIO 文件存储服务

基于 ASP.NET Core + MinIO 的分布式文件存储服务，支持文件上传下载、版本管理、断点续传、本地缓存等功能。

## 功能特性

### 核心功能
- **文件存储**：支持 MinIO 对象存储和本地文件系统两种后端
- **版本管理**：上传同名文件自动创建版本链，支持历史版本恢复
- **断点续传**：大文件分片上传和下载，支持断点续传
- **本地缓存**：文件下载后自动缓存到本地，提高访问速度
- **文件夹管理**：支持虚拟文件夹结构（前缀模拟）
- **Bucket 管理**：支持多 Bucket 创建和管理

### 技术特性
- **存储后端可切换**：通过配置在 MinIO 和本地存储间切换
- **数据库记录**：使用 SQLite 记录文件元数据和版本信息
- **Swagger API 文档**：自动生成 API 测试界面
- **Docker 部署**：提供 MinIO Docker Compose 配置

## 技术栈

- **.NET 10.0** - 后端框架
- **MinIO SDK 7.0** - 对象存储客户端
- **Entity Framework Core 10.0** - ORM 框架
- **SQLite** - 本地数据库
- **Swashbuckle.AspNetCore** - Swagger 文档生成

## 项目结构

```
MinIOManger/
├── Controllers/              # API 控制器
│   ├── FilesController.cs           # 文件上传下载（MinIO 直连）
│   ├── BucketsController.cs         # Bucket 管理
│   ├── FoldersController.cs         # 文件夹管理
│   ├── FileCacheController.cs       # 文件缓存服务（数据库+本地缓存）
│   ├── FileVersionsController.cs    # 版本管理
│   ├── ChunkedUploadController.cs   # 分片上传（断点续传）
│   └── ChunkedDownloadController.cs # 分片下载（断点续传）
├── Services/                 # 业务服务
│   ├── IStorageProvider.cs          # 存储提供者接口
│   ├── LocalFileProvider.cs         # 本地文件系统实现
│   ├── MinioStorageProvider.cs      # MinIO 实现
│   ├── IMinioService.cs             # MinIO 服务接口
│   ├── MinioService.cs              # MinIO 服务实现
│   ├── IFileCacheService.cs         # 文件缓存服务接口
│   ├── FileCacheService.cs          # 文件缓存服务实现
│   ├── IFileVersionService.cs       # 版本服务接口
│   ├── FileVersionService.cs        # 版本服务实现
│   ├── IChunkedUploadService.cs     # 分片上传接口
│   ├── ChunkedUploadService.cs      # 分片上传实现
│   ├── IChunkedDownloadService.cs   # 分片下载接口
│   └── RangeStream.cs               # 范围流包装器
├── Data/                     # 数据访问
│   ├── FileDbContext.cs             # 数据库上下文
│   └── Entities/                    # 实体类
│       ├── FileRecord.cs            # 文件记录
│       ├── FileVersion.cs           # 文件版本
│       ├── UploadSession.cs         # 上传会话
│       └── DownloadTask.cs          # 下载任务
├── Models/                   # 数据模型
│   ├── MinIOSettings.cs             # MinIO 配置
│   ├── StorageSettings.cs           # 存储配置
│   └── FileMetadata.cs              # 文件元数据
├── docs/                     # 文档
│   └── 断点续传下载方案对比.md       # 技术方案文档
├── docker-compose.yml        # Docker 部署配置
├── start.ps1 / start.bat     # 启动脚本
├── stop.ps1 / stop.bat       # 停止脚本
└── Program.cs                # 程序入口
```

## 快速开始

### 1. 环境要求
- .NET 10.0 SDK
- Docker（可选，用于运行 MinIO）

### 2. 配置
编辑 `appsettings.json`：

```json
{
  "Storage": {
    "Provider": "MinIO",      // 或 "Local" 使用本地存储
    "LocalPath": "Storage",   // 本地存储路径
    "MinIO": {
      "Endpoint": "localhost:9000",
      "AccessKey": "minioadmin",
      "SecretKey": "minioadmin",
      "BucketName": "files",
      "UseSSL": false
    }
  }
}
```

### 3. 启动服务

**使用 MinIO（推荐）：**
```powershell
# 启动 MinIO 容器
./start.ps1

# 或手动启动
docker-compose up -d

# 运行服务
dotnet run
```

**使用本地存储：**
```powershell
dotnet run
```

### 4. 访问 Swagger UI
```
http://localhost:5000/swagger
```

## API 概览

### MinIO 对象存储 API
| 端点 | 说明 |
|------|------|
| `GET /api/files` | 列出文件 |
| `POST /api/files/upload` | 上传文件 |
| `GET /api/files/download/{objectName}` | 下载文件 |
| `DELETE /api/files/{objectName}` | 删除文件 |
| `GET /api/buckets` | 列出 Buckets |
| `POST /api/buckets/{bucketName}` | 创建 Bucket |
| `DELETE /api/buckets/{bucketName}` | 删除 Bucket |
| `GET /api/folders` | 列出文件夹 |
| `POST /api/folders/{folderPath}` | 创建文件夹 |

### 文件缓存与版本管理 API
| 端点 | 说明 |
|------|------|
| `POST /api/filecache/upload` | 上传文件（创建版本） |
| `GET /api/filecache/download/{fileId}` | 下载最新版本 |
| `GET /api/filecache/list` | 列出文件记录 |
| `GET /api/filecache/{fileId}` | 获取文件详情（含版本） |
| `DELETE /api/filecache/{fileId}` | 软删除文件 |
| `GET /api/filecache/{fileId}/versions` | 获取版本列表 |
| `POST /api/filecache/{fileId}/versions/{versionId}/restore` | 恢复版本 |
| `GET /api/filecache/{fileId}/versions/{versionId}/download` | 下载指定版本 |

### 分片上传 API（断点续传）
| 端点 | 说明 |
|------|------|
| `POST /api/upload/init` | 初始化上传 |
| `POST /api/upload/quick` | 尝试秒传 |
| `PUT /api/upload/{uploadId}/chunks/{chunkNumber}` | 上传分片 |
| `GET /api/upload/{uploadId}/status` | 获取上传状态 |
| `POST /api/upload/{uploadId}/complete` | 完成上传 |
| `DELETE /api/upload/{uploadId}` | 取消上传 |

### 分片下载 API（断点续传）
| 端点 | 说明 |
|------|------|
| `GET /api/download/{versionId}/info` | 获取文件信息 |
| `GET /api/download/{versionId}` | 下载文件（支持 Range） |
| `GET /api/download/{versionId}/chunks` | 获取分片下载计划 |
| `GET /api/download/{versionId}/chunks/{index}` | 获取单个分片 URL |

## 核心功能说明

### 1. 存储后端切换
通过修改 `appsettings.json` 中的 `Storage:Provider` 配置：
- `"  ,"
- `"Local"` - 使用本地文件系统

无需修改代码即可切换存储后端。

### 2. 版本链管理
- 上传同名文件自动创建新版本
- 保留历史版本，支持恢复到任意版本
- 支持软删除和彻底删除

### 3. 断点续传
**上传：**
1. 初始化上传会话（支持秒传检查）
2. 分片上传（可并行）
3. 完成上传后自动合并

**下载：**
1. HTTP Range 请求（方案1）
2. 预签名 URL 分片下载（方案2）

### 4. 本地缓存
- 文件下载后自动缓存到本地
- 再次下载时校验哈希，一致则直接返回缓存
- 支持清理过期缓存

## 数据库实体关系

```
FileRecord (文件记录)
    ├── Id, FileName, ContentType
    ├── CurrentVersionId → FileVersion
    ├── VersionCount
    └── Versions[] → FileVersion[]

FileVersion (文件版本)
    ├── Id, VersionNumber
    ├── FileRecordId → FileRecord
    ├── ObjectName (存储路径)
    ├── FileHash (SHA256)
    ├── Size
    ├── IsLatest
    └── IsDeleted

UploadSession (上传会话)
    ├── Id, FileName, FileSize
    ├── TotalChunks, UploadedChunks
    ├── TempPath (临时存储)
    ├── Status
    └── ExpiresAt

DownloadTask (下载任务)
    ├── Id, FileVersionId → FileVersion
    ├── ClientId
    ├── DownloadedBytes
    ├── Status
    └── ExpiresAt
```

## 部署

### Docker Compose
```yaml
services:
  minio:
    image: minio/minio:latest
    ports:
      - "9000:9000"
      - "9001:9001"
    environment:
      MINIO_ROOT_USER: minioadmin
      MINIO_ROOT_PASSWORD: minioadmin
    command: server /data --console-address ":9001"
```

### 生产环境建议
1. 使用反向代理（Nginx/IIS）
2. 配置 HTTPS
3. 设置适当的文件大小限制
4. 配置日志和监控
5. 定期备份数据库

## 开发计划

- [x] 基础文件上传下载
- [x] Bucket 和文件夹管理
- [x] 本地缓存服务
- [x] 版本链管理
- [x] 分片上传（断点续传）
- [x] 分片下载（断点续传）
- [x] 存储后端可切换
- [x] Swagger API 文档
- [ ] 文件预览功能
- [ ] 图片缩略图
- [ ] 视频流媒体
- [ ] 权限管理
- [ ] 统计报表

## 许可证

MIT License
