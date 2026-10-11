using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddApiDeploymentTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    ServerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HostName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false, defaultValue: 22),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EncryptedPassword = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SshPrivateKey = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DeploymentPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AppPoolName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WebsiteName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BackupPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: "C:\\DeployVare\\Backups"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                });

            migrationBuilder.CreateTable(
                name: "ApiDeployments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationServerId = table.Column<int>(type: "int", nullable: false),
                    DeployedBy = table.Column<int>(type: "int", nullable: false),
                    DeploymentDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    BackupFileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LocalBackupPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeploymentDurationSeconds = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                name: "ApiDeploymentFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApiDeploymentId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    FileHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
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
                name: "UQ_Application_Server",
                table: "ApplicationServers",
                columns: new[] { "ApplicationId", "ServerName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiDeploymentFiles");

            migrationBuilder.DropTable(
                name: "ApiDeployments");

            migrationBuilder.DropTable(
                name: "ApplicationServers");
        }
    }
}
