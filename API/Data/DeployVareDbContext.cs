using API.Models.Entities;
using API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class DeployVareDbContext : DbContext
{
    public DeployVareDbContext(DbContextOptions<DeployVareDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<UserPermission> UserPermissions { get; set; }
    public DbSet<Application> Applications { get; set; }
    public DbSet<DatabaseInstance> DatabaseInstances { get; set; }
    public DbSet<Deployment> Deployments { get; set; }
    public DbSet<DeploymentPatch> DeploymentPatches { get; set; }
    public DbSet<DeploymentObject> DeploymentObjects { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    
    // API Deployment entities
    public DbSet<ApplicationServer> ApplicationServers { get; set; }
    public DbSet<ApiDeployment> ApiDeployments { get; set; }
    public DbSet<ApiDeploymentFile> ApiDeploymentFiles { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User Configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.FullName).HasMaxLength(200);
            entity.Property(e => e.Role)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // UserPermission Configuration
        modelBuilder.Entity<UserPermission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Permission)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.GrantedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.GrantedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserPermissions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Permission })
                .IsUnique()
                .HasDatabaseName("UQ_User_Permission");
        });

        // Application Configuration
        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.Name).IsUnique();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        });

        // DatabaseInstance Configuration
        modelBuilder.Entity<DatabaseInstance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.InstanceName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ConnectionStringEncrypted).IsRequired();
            entity.Property(e => e.CurrentPatchNumber).HasDefaultValue(0);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UpdatedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            
            entity.HasIndex(e => new { e.ApplicationId, e.InstanceName })
                .IsUnique()
                .HasDatabaseName("UQ_Application_Instance");

            entity.HasOne(e => e.Application)
                .WithMany(a => a.DatabaseInstances)
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Deployment Configuration
        modelBuilder.Entity<Deployment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeployedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.BackupFilePath).HasMaxLength(500);
            entity.Property(e => e.DeployedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.DatabaseInstance)
                .WithMany(d => d.Deployments)
                .HasForeignKey(e => e.DatabaseInstanceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.DatabaseInstanceId);
            entity.HasIndex(e => e.DeployedAt).IsDescending();
        });

        // DeploymentPatch Configuration
        modelBuilder.Entity<DeploymentPatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PatchFileName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.Author).HasMaxLength(100);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20);
            entity.Property(e => e.ExecutedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Deployment)
                .WithMany(d => d.DeploymentPatches)
                .HasForeignKey(e => e.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DeploymentId);
        });

        // DeploymentObject Configuration
        modelBuilder.Entity<DeploymentObject>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ObjectType).IsRequired().HasMaxLength(50);
            entity.Property(e => e.ObjectName).IsRequired().HasMaxLength(255);
            entity.Property(e => e.ExecutedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Deployment)
                .WithMany(d => d.DeploymentObjects)
                .HasForeignKey(e => e.DeploymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.DeploymentId);
        });

        // AuditLog Configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.UserId).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Action).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EntityType).HasMaxLength(50);
            entity.Property(e => e.IpAddress).HasMaxLength(50);
            entity.Property(e => e.Timestamp).HasDefaultValueSql("GETUTCDATE()");

            entity.HasIndex(e => e.Timestamp).IsDescending();
        });

        // ApplicationServer Configuration
        modelBuilder.Entity<ApplicationServer>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ServerName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.HostName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Port).HasDefaultValue(22);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EncryptedPassword).HasMaxLength(500);
            entity.Property(e => e.DeploymentPath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.AppPoolName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.WebsiteName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.BackupPath).HasMaxLength(500).HasDefaultValue(@"C:\DeployVare\Backups");
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.Application)
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.ApplicationId, e.ServerName })
                .IsUnique()
                .HasDatabaseName("UQ_Application_Server");
        });

        // ApiDeployment Configuration
        modelBuilder.Entity<ApiDeployment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Version).HasMaxLength(50);
            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(50);
            entity.Property(e => e.BackupFileName).HasMaxLength(500);
            entity.Property(e => e.LocalBackupPath).HasMaxLength(500);
            entity.Property(e => e.DeploymentDate).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.ApplicationServer)
                .WithMany(s => s.ApiDeployments)
                .HasForeignKey(e => e.ApplicationServerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DeployedByUser)
                .WithMany()
                .HasForeignKey(e => e.DeployedBy)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.ApplicationServerId);
            entity.HasIndex(e => e.DeploymentDate).IsDescending();
        });

        // ApiDeploymentFile Configuration
        modelBuilder.Entity<ApiDeploymentFile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.FileHash).HasMaxLength(64);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("GETUTCDATE()");

            entity.HasOne(e => e.ApiDeployment)
                .WithMany(d => d.DeploymentFiles)
                .HasForeignKey(e => e.ApiDeploymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ApiDeploymentId);
        });
    }
}

