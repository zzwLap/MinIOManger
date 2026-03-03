using Microsoft.EntityFrameworkCore;
using MinIOStorageService.Data.Entities;

namespace MinIOStorageService.Data;

public class FileDbContext : DbContext
{
    public FileDbContext(DbContextOptions<FileDbContext> options) : base(options)
    {
    }

    public DbSet<FileRecord> FileRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ObjectName).IsUnique();
            entity.HasIndex(e => e.FileHash);
            entity.Property(e => e.FileName).HasMaxLength(500);
            entity.Property(e => e.ObjectName).HasMaxLength(1000);
            entity.Property(e => e.ContentType).HasMaxLength(200);
            entity.Property(e => e.FileHash).HasMaxLength(64);
            entity.Property(e => e.LocalCachePath).HasMaxLength(1000);
        });
    }
}
