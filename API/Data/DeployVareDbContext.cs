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

    // For provider-specific subclasses (PostgresDeployVareDbContext), which carry their own migrations
    protected DeployVareDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<UserPermission> UserPermissions { get; set; }
    public DbSet<Client> Clients { get; set; }
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

    // Azure DevOps integration entities
    public DbSet<AzureDevOpsConnection> AzureDevOpsConnections { get; set; }
    public DbSet<ArtifactSource> ArtifactSources { get; set; }

    // Server setup
    public DbSet<ServerReadinessCheck> ServerReadinessChecks { get; set; }

    // Licensing
    public DbSet<License> Licenses { get; set; }
    public DbSet<LicenseActivation> LicenseActivations { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Use the correct SQL function for the active database provider
        var isSqlite = Database.IsSqlite();
        var isPostgres = Database.IsNpgsql();
        var nowSql = isSqlite ? "datetime('now')" : isPostgres ? "(now() at time zone 'utc')" : "GETUTCDATE()";

        // Licence Configuration
        modelBuilder.Entity<License>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LicenseId).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => e.LicenseId).IsUnique();
            entity.Property(e => e.Product).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(e => e.Payload).IsRequired();
            entity.Property(e => e.Signature).IsRequired();
            entity.Property(e => e.KeyId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Features).HasMaxLength(2000);
            entity.Property(e => e.Notes).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);

            entity.HasIndex(e => new { e.ClientId, e.Product });

            entity.HasOne(e => e.Client)
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Licence Activation Configuration
        modelBuilder.Entity<LicenseActivation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MachinePublicKey).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.MachineFingerprint).IsRequired().HasMaxLength(64);
            entity.Property(e => e.MachineName).HasMaxLength(200);
            entity.Property(e => e.ServerIdentity).HasMaxLength(500);
            entity.Property(e => e.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(e => e.ActivatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.DeactivationReason).HasMaxLength(600);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);

            entity.HasOne(e => e.License)
                .WithMany(l => l.Activations)
                .HasForeignKey(e => e.LicenseId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per machine per licence, so re-activating the same server is idempotent
            entity.HasIndex(e => new { e.LicenseId, e.MachineFingerprint })
                .IsUnique()
                .HasDatabaseName("UQ_License_Machine");

            // The database, not application code, enforces that only one machine holds a licence at a time
            entity.HasIndex(e => e.LicenseId)
                .IsUnique()
                .HasFilter(isSqlite || isPostgres ? "\"Status\" = 'Active'" : "[Status] = 'Active'")
                .HasDatabaseName("UQ_License_ActiveActivation");
        });

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
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);
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
            entity.Property(e => e.GrantedAt).HasDefaultValueSql(nowSql);

            entity.HasOne(e => e.User)
                .WithMany(u => u.UserPermissions)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.UserId, e.Permission })
                .IsUnique()
                .HasDatabaseName("UQ_User_Permission");
        });

        // Client Configuration
        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ClientCode).IsRequired().HasMaxLength(50);
            entity.HasIndex(e => e.ClientCode).IsUnique();
            entity.Property(e => e.LicenseKey).IsRequired().HasMaxLength(32);
            entity.HasIndex(e => e.LicenseKey).IsUnique();
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);
        });

        // Application Configuration
        modelBuilder.Entity<Application>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);

            // Application names are unique per client
            entity.HasIndex(e => new { e.ClientId, e.Name })
                .IsUnique()
                .HasDatabaseName("UQ_Client_Application");

            entity.HasOne(e => e.Client)
                .WithMany(c => c.Applications)
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);
            
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
            entity.Property(e => e.DeployedAt).HasDefaultValueSql(nowSql);

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
            entity.Property(e => e.ExecutedAt).HasDefaultValueSql(nowSql);

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
            entity.Property(e => e.ExecutedAt).HasDefaultValueSql(nowSql);

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
            entity.Property(e => e.Timestamp).HasDefaultValueSql(nowSql);

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
            entity.Property(e => e.ServerType)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValueSql("'Api'");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);

            entity.HasOne(e => e.Application)
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.DatabaseInstance)
                .WithMany()
                .HasForeignKey(e => e.DatabaseInstanceId)
                .OnDelete(DeleteBehavior.SetNull);

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
            entity.Property(e => e.DeploymentType)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValueSql("'Api'");

            // Stored as text and defaulted in SQL, so every deployment recorded before licensing existed reads
            // back as WithoutLicense rather than null - which is exactly what those deployments were.
            entity.Property(e => e.LicensingMode)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValueSql("'WithoutLicense'");
            entity.Property(e => e.LicenseId).HasMaxLength(50);

            entity.Property(e => e.DeploymentDate).HasDefaultValueSql(nowSql);

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
            entity.Property(e => e.UploadedAt).HasDefaultValueSql(nowSql);

            entity.HasOne(e => e.ApiDeployment)
                .WithMany(d => d.DeploymentFiles)
                .HasForeignKey(e => e.ApiDeploymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.ApiDeploymentId);
        });

        // AzureDevOpsConnection Configuration (one per client)
        modelBuilder.Entity<AzureDevOpsConnection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Organization).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Project).IsRequired().HasMaxLength(200);
            entity.Property(e => e.EncryptedPersonalAccessToken).IsRequired();
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UpdatedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);

            entity.HasIndex(e => e.ClientId).IsUnique();

            entity.HasOne(e => e.Client)
                .WithMany()
                .HasForeignKey(e => e.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ArtifactSource Configuration (one per application + environment + server type)
        modelBuilder.Entity<ArtifactSource>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Environment)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.ServerType)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);
            entity.Property(e => e.Repository).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Branch).IsRequired().HasMaxLength(250);
            entity.Property(e => e.ArtifactName).HasMaxLength(200);
            entity.Property(e => e.ArtifactSubfolder).HasMaxLength(500);
            entity.Property(e => e.KeepOnServerPaths).HasMaxLength(4000);
            entity.Property(e => e.SqlScriptsArtifactName).HasMaxLength(200);
            entity.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.UpdatedBy).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(nowSql);

            entity.HasIndex(e => new { e.ApplicationId, e.Environment, e.ServerType })
                .IsUnique()
                .HasDatabaseName("UQ_Application_Environment_ServerType");

            entity.HasOne(e => e.Application)
                .WithMany()
                .HasForeignKey(e => e.ApplicationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ServerReadinessCheck Configuration (latest report per server)
        modelBuilder.Entity<ServerReadinessCheck>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CheckedBy).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ReportJson).IsRequired();

            entity.HasIndex(e => e.ApplicationServerId).IsUnique();

            entity.HasOne(e => e.ApplicationServer)
                .WithMany()
                .HasForeignKey(e => e.ApplicationServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

