using Microsoft.EntityFrameworkCore;
using Minio;
using MinIOStorageService.Data;
using MinIOStorageService.Hubs;
using MinIOStorageService.Models;
using MinIOStorageService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add Swagger with multiple API groups
builder.Services.AddSwaggerGen(c =>
{
    // MinIO 存储服务 API
    c.SwaggerDoc("minio", new()
    {
        Title = "MinIO 对象存储 API",
        Version = "v1",
        Description = "基于 MinIO 的对象存储服务，支持文件上传、下载、Bucket 和文件夹管理"
    });

    // 文件缓存服务 API
    c.SwaggerDoc("filecache", new()
    {
        Title = "文件缓存与版本管理 API",
        Version = "v1",
        Description = "基于数据库的文件缓存服务，支持版本管理、本地缓存、历史版本恢复"
    });

    // 配置 API 分组
    c.DocInclusionPredicate((docName, apiDesc) =>
    {
        var controllerName = apiDesc.ActionDescriptor.RouteValues["controller"]?.ToLower();
        
        return docName.ToLower() switch
        {
            "minio" => controllerName is "files" or "buckets" or "folders",
            "filecache" => controllerName is "filecache" or "fileversions" or "chunkedupload" or "chunkeddownload" or "p2pdownload",
            _ => false
        };
    });

    // 添加中文注释支持
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// Configure Database
var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FileStorage.db");
builder.Services.AddDbContext<FileDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Configure Storage Provider
var storageSettings = builder.Configuration.GetSection("Storage").Get<StorageSettings>()!;
builder.Services.AddSingleton(storageSettings);

// 根据配置注册不同的存储提供者
if (storageSettings.Provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
{
    // 本地文件存储模式
    var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, storageSettings.LocalPath);
    builder.Services.AddSingleton<IStorageProvider>(sp => 
    {
        var logger = sp.GetRequiredService<ILogger<LocalFileProvider>>();
        return new LocalFileProvider(localPath, logger);
    });
    
    // 为兼容原有代码，仍然注册 MinIO 相关服务（但不会被使用）
    builder.Services.AddSingleton<MinIOSettings>(storageSettings.MinIO);
    builder.Services.AddScoped<IMinioService, MinioService>();
}
else
{
    // MinIO 存储模式（默认）
    var minioSettings = storageSettings.MinIO;
    builder.Services.AddSingleton(minioSettings);
    
    // Create MinIO client
    var minioClient = new MinioClient()
        .WithEndpoint(minioSettings.Endpoint)
        .WithCredentials(minioSettings.AccessKey, minioSettings.SecretKey);

    if (minioSettings.UseSSL)
    {
        minioClient = minioClient.WithSSL();
    }

    builder.Services.AddSingleton<IMinioClient>(minioClient.Build());
    builder.Services.AddScoped<IMinioService, MinioService>();
    
    // 注册 MinIO 存储提供者
    builder.Services.AddSingleton<IStorageProvider>(sp =>
    {
        var client = sp.GetRequiredService<IMinioClient>();
        var logger = sp.GetRequiredService<ILogger<MinioStorageProvider>>();
        return new MinioStorageProvider(client, minioSettings.BucketName, logger);
    });
}

// Add File Cache Service（使用 IStorageProvider 抽象）
builder.Services.AddScoped<IFileCacheService, FileCacheService>();

// Add File Version Service（版本管理服务）
builder.Services.AddScoped<IFileVersionService, FileVersionService>();

// Add Chunked Upload Service（分片上传服务）
builder.Services.AddScoped<IChunkedUploadService, ChunkedUploadService>();

// Add Chunked Download Service（分片下载服务 - 服务端代理）
builder.Services.AddScoped<IChunkedDownloadService, ChunkedDownloadService>();

// Add P2P Download Service（P2P分片下载服务）
builder.Services.AddScoped<IP2PDownloadService, P2PDownloadService>();

// Add SignalR for P2P real-time communication
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        // 配置多个 Swagger 文档入口
        c.SwaggerEndpoint("/swagger/minio/swagger.json", "MinIO 对象存储 API");
        c.SwaggerEndpoint("/swagger/filecache/swagger.json", "文件缓存与版本管理 API");
        c.DocumentTitle = "文件存储服务 - Swagger UI";
        
        // 默认展开所有 API
        c.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
    });
}
//由于这个把http转成https,触发同源策略
app.UseHttpsRedirection();

// Use CORS before other middleware
app.UseCors("AllowAll");

app.UseAuthorization();
app.MapControllers();

// Map SignalR hubs
app.MapHub<P2PTrackerHub>("/p2phub");

// 自动创建数据库
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FileDbContext>();
    dbContext.Database.EnsureCreated();
}

app.Run();
