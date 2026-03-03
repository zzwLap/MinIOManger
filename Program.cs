using Minio;
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
        Description = "基于 ASP.NET Core 和 MinIO 的文件上传、下载、管理 API"
    });
});

// Configure MinIO
var minioSettings = builder.Configuration.GetSection("MinIO").Get<MinIOSettings>()!;
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

app.Run();
