using Microsoft.EntityFrameworkCore;
using Minio;
using MinIOStorageService.Data;
using MinIOStorageService.Models;
using MinIOStorageService.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Add Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "MinIO 文件存储服务 API",
        Version = "v1",
        Description = "基于 ASP.NET Core 和 MinIO 的文件上传、下载、管理 API，支持数据库记录和本地缓存"
    });
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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MinIO 文件存储服务 API V1");
        c.DocumentTitle = "MinIO 文件存储服务 - Swagger UI";
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// 自动创建数据库
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<FileDbContext>();
    dbContext.Database.EnsureCreated();
}

app.Run();
