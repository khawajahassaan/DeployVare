using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Applications_Name",
                table: "Applications");

            migrationBuilder.AddColumn<int>(
                name: "ClientId",
                table: "Applications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClientCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LicenseKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            // Existing applications were created before clients existed: move them under a
            // "Default Client" (rename it from Client Onboarding) so the FK below can be added.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM [Applications])
BEGIN
    DECLARE @DefaultClientId int;

    INSERT INTO [Clients] ([Name], [ClientCode], [LicenseKey], [IsActive], [CreatedAt], [CreatedBy])
    VALUES ('Default Client', 'DEFAULT', UPPER(REPLACE(CONVERT(nvarchar(36), NEWID()), '-', '')), 1, GETUTCDATE(), 'system');

    SET @DefaultClientId = SCOPE_IDENTITY();

    UPDATE [Applications] SET [ClientId] = @DefaultClientId WHERE [ClientId] = 0;
END");

            migrationBuilder.CreateIndex(
                name: "UQ_Client_Application",
                table: "Applications",
                columns: new[] { "ClientId", "Name" },
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

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Clients_ClientId",
                table: "Applications",
                column: "ClientId",
                principalTable: "Clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Applications_Clients_ClientId",
                table: "Applications");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropIndex(
                name: "UQ_Client_Application",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "Applications");

            migrationBuilder.CreateIndex(
                name: "IX_Applications_Name",
                table: "Applications",
                column: "Name",
                unique: true);
        }
    }
}
