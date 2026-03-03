using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Data;

public class FileDbContext : DbContext
{
    public FileDbContext(DbContextOptions<FileDbContext> options) : base(options)
    {
    }

    public DbSet<FileRecord> FileRecords { get; set; }
    public DbSet<FileVersion> FileVersions { get; set; }
    public DbSet<UploadSession> UploadSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // FileRecord 配置
        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FileName);
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.ContentType).HasMaxLength(200);
            
            // 与 FileVersion 的关系
            entity.HasMany(e => e.Versions)
                  .WithOne(v => v.FileRecord)
                  .HasForeignKey(v => v.FileRecordId)
                  .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasOne(e => e.CurrentVersion)
                  .WithOne()
                  .HasForeignKey<FileRecord>(e => e.CurrentVersionId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // FileVersion 配置
        modelBuilder.Entity<FileVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FileRecordId);
            entity.HasIndex(e => e.ObjectName).IsUnique();
            entity.HasIndex(e => e.FileHash);
            entity.HasIndex(e => new { e.FileRecordId, e.VersionNumber }).IsUnique();
            entity.HasIndex(e => e.IsLatest);
            entity.HasIndex(e => e.IsDeleted);
            
            entity.Property(e => e.ObjectName).HasMaxLength(1000);
            entity.Property(e => e.FileHash).HasMaxLength(64);
            entity.Property(e => e.LocalCachePath).HasMaxLength(1000);
            entity.Property(e => e.CreatedBy).HasMaxLength(200);
            entity.Property(e => e.ChangeDescription).HasMaxLength(500);
        });

        // UploadSession 配置
        modelBuilder.Entity<UploadSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.FileHash);
            entity.HasIndex(e => e.FileRecordId);
            
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.ContentType).HasMaxLength(200);
            entity.Property(e => e.FileHash).HasMaxLength(64);
            entity.Property(e => e.Folder).HasMaxLength(1000);
            entity.Property(e => e.TempPath).HasMaxLength(1000);
            entity.Property(e => e.UploadedChunks).HasMaxLength(4000);
            
            entity.HasOne(e => e.FileRecord)
                  .WithMany()
                  .HasForeignKey(e => e.FileRecordId)
                  .OnDelete(DeleteBehavior.SetNull);
                  
            entity.HasOne(e => e.FileVersion)
                  .WithMany()
                  .HasForeignKey(e => e.FileVersionId)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
