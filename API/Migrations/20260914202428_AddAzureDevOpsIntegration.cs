using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureDevOpsIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtifactEnvironment",
                table: "ApiDeployments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureArtifactName",
                table: "ApiDeployments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AzureBuildId",
                table: "ApiDeployments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureBuildNumber",
                table: "ApiDeployments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceBranch",
                table: "ApiDeployments",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceVersion",
                table: "ApiDeployments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArtifactSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ServerType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Repository = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ArtifactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PipelineDefinitionId = table.Column<int>(type: "int", nullable: true),
                    ArtifactSubfolder = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PreserveWebConfig = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
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
                name: "AzureDevOpsConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    Organization = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Project = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EncryptedPersonalAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastTestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastTestSucceeded = table.Column<bool>(type: "bit", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "UQ_Application_Environment_ServerType",
                table: "ArtifactSources",
                columns: new[] { "ApplicationId", "Environment", "ServerType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AzureDevOpsConnections_ClientId",
                table: "AzureDevOpsConnections",
                column: "ClientId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactSources");

            migrationBuilder.DropTable(
                name: "AzureDevOpsConnections");

            migrationBuilder.DropColumn(
                name: "ArtifactEnvironment",
                table: "ApiDeployments");

            migrationBuilder.DropColumn(
                name: "AzureArtifactName",
                table: "ApiDeployments");

            migrationBuilder.DropColumn(
                name: "AzureBuildId",
                table: "ApiDeployments");

            migrationBuilder.DropColumn(
                name: "AzureBuildNumber",
                table: "ApiDeployments");

            migrationBuilder.DropColumn(
                name: "SourceBranch",
                table: "ApiDeployments");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "ApiDeployments");
        }
    }
}
