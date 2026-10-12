using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace API.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EntityId = table.Column<int>(type: "integer", nullable: true),
                    Details = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    IpAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ClientCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LicenseKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Applications_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AzureDevOpsConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    Organization = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Project = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EncryptedPersonalAccessToken = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastTestedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureDevOpsConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AzureDevOpsConnections_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Licenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    LicenseId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Product = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    MaxActivations = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Signature = table.Column<string>(type: "text", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Features = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Licenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Licenses_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Permission = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    GrantedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtifactSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ServerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Repository = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Branch = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ArtifactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PipelineDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    ArtifactSubfolder = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PreserveWebConfig = table.Column<bool>(type: "boolean", nullable: false),
                    KeepOnServerPaths = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    RunDatabaseMigrations = table.Column<bool>(type: "boolean", nullable: false),
                    SqlScriptsArtifactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArtifactSources_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DatabaseInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    InstanceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConnectionStringEncrypted = table.Column<string>(type: "text", nullable: false),
                    CurrentPatchNumber = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatabaseInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatabaseInstances_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenseActivations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicenseId = table.Column<int>(type: "integer", nullable: false),
                    MachinePublicKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    MachineFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MachineName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ServerIdentity = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastIssuedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ResponsePayload = table.Column<string>(type: "text", nullable: false),
                    ResponseSignature = table.Column<string>(type: "text", nullable: false),
                    ActivatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeactivationReason = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseActivations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseActivations_Licenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "Licenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationId = table.Column<int>(type: "integer", nullable: false),
                    ServerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    HostName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false, defaultValue: 22),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SshPrivateKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DeploymentPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AppPoolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WebsiteName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BackupPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false, defaultValue: "C:\\DeployVare\\Backups"),
                    ServerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'Api'"),
                    RuntimeConfigJson = table.Column<string>(type: "text", nullable: true),
                    DatabaseInstanceId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationServers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationServers_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApplicationServers_DatabaseInstances_DatabaseInstanceId",
                        column: x => x.DatabaseInstanceId,
                        principalTable: "DatabaseInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Deployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DatabaseInstanceId = table.Column<int>(type: "integer", nullable: false),
                    DeployedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeployedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FromPatchNumber = table.Column<int>(type: "integer", nullable: false),
                    ToPatchNumber = table.Column<int>(type: "integer", nullable: false),
                    BackupFilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deployments_DatabaseInstances_DatabaseInstanceId",
                        column: x => x.DatabaseInstanceId,
                        principalTable: "DatabaseInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApiDeployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationServerId = table.Column<int>(type: "integer", nullable: false),
                    DeployedBy = table.Column<int>(type: "integer", nullable: false),
                    DeploymentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BackupFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LocalBackupPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DeploymentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'Api'"),
                    LicensingMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'WithoutLicense'"),
                    LicenseId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ArtifactEnvironment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    AzureBuildId = table.Column<int>(type: "integer", nullable: true),
                    AzureBuildNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AzureArtifactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SourceBranch = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    SourceVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    DeploymentDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiDeployments_ApplicationServers_ApplicationServerId",
                        column: x => x.ApplicationServerId,
                        principalTable: "ApplicationServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ApiDeployments_Users_DeployedBy",
                        column: x => x.DeployedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServerReadinessChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationServerId = table.Column<int>(type: "integer", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CheckedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsReady = table.Column<bool>(type: "boolean", nullable: false),
                    ReportJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerReadinessChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerReadinessChecks_ApplicationServers_ApplicationServerId",
                        column: x => x.ApplicationServerId,
                        principalTable: "ApplicationServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentObjects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeploymentId = table.Column<int>(type: "integer", nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ObjectName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    BackedUp = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentObjects_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeploymentPatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeploymentId = table.Column<int>(type: "integer", nullable: false),
                    PatchNumber = table.Column<int>(type: "integer", nullable: false),
                    PatchFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Author = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')"),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentPatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeploymentPatches_Deployments_DeploymentId",
                        column: x => x.DeploymentId,
                        principalTable: "Deployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiDeploymentFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApiDeploymentId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "(now() at time zone 'utc')")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiDeploymentFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiDeploymentFiles_ApiDeployments_ApiDeploymentId",
                        column: x => x.ApiDeploymentId,
                        principalTable: "ApiDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiDeploymentFiles_ApiDeploymentId",
                table: "ApiDeploymentFiles",
                column: "ApiDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiDeployments_ApplicationServerId",
                table: "ApiDeployments",
                column: "ApplicationServerId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiDeployments_DeployedBy",
                table: "ApiDeployments",
                column: "DeployedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ApiDeployments_DeploymentDate",
                table: "ApiDeployments",
                column: "DeploymentDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "UQ_Client_Application",
                table: "Applications",
                columns: new[] { "ClientId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationServers_DatabaseInstanceId",
                table: "ApplicationServers",
                column: "DatabaseInstanceId");

            migrationBuilder.CreateIndex(
                name: "UQ_Application_Server",
                table: "ApplicationServers",
                columns: new[] { "ApplicationId", "ServerName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Application_Environment_ServerType",
                table: "ArtifactSources",
                columns: new[] { "ApplicationId", "Environment", "ServerType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AzureDevOpsConnections_ClientId",
                table: "AzureDevOpsConnections",
                column: "ClientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_ClientCode",
                table: "Clients",
                column: "ClientCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_LicenseKey",
                table: "Clients",
                column: "LicenseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_Application_Instance",
                table: "DatabaseInstances",
                columns: new[] { "ApplicationId", "InstanceName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentObjects_DeploymentId",
                table: "DeploymentObjects",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_DeploymentPatches_DeploymentId",
                table: "DeploymentPatches",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_DatabaseInstanceId",
                table: "Deployments",
                column: "DatabaseInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_Deployments_DeployedAt",
                table: "Deployments",
                column: "DeployedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "UQ_License_ActiveActivation",
                table: "LicenseActivations",
                column: "LicenseId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "UQ_License_Machine",
                table: "LicenseActivations",
                columns: new[] { "LicenseId", "MachineFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_ClientId_Product",
                table: "Licenses",
                columns: new[] { "ClientId", "Product" });

            migrationBuilder.CreateIndex(
                name: "IX_Licenses_LicenseId",
                table: "Licenses",
                column: "LicenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerReadinessChecks_ApplicationServerId",
                table: "ServerReadinessChecks",
                column: "ApplicationServerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UQ_User_Permission",
                table: "UserPermissions",
                columns: new[] { "UserId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiDeploymentFiles");

            migrationBuilder.DropTable(
                name: "ArtifactSources");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "AzureDevOpsConnections");

            migrationBuilder.DropTable(
                name: "DeploymentObjects");

            migrationBuilder.DropTable(
                name: "DeploymentPatches");

            migrationBuilder.DropTable(
                name: "LicenseActivations");

            migrationBuilder.DropTable(
                name: "ServerReadinessChecks");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "ApiDeployments");

            migrationBuilder.DropTable(
                name: "Deployments");

            migrationBuilder.DropTable(
                name: "Licenses");

            migrationBuilder.DropTable(
                name: "ApplicationServers");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "DatabaseInstances");

            migrationBuilder.DropTable(
                name: "Applications");

            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
