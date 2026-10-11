using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddLicensing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Licenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<int>(type: "int", nullable: false),
                    LicenseId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Product = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MaxActivations = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    KeyId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Features = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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
                name: "LicenseActivations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LicenseId = table.Column<int>(type: "int", nullable: false),
                    MachinePublicKey = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    MachineFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MachineName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ServerIdentity = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeactivatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastIssuedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResponsePayload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseSignature = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeactivationReason = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "UQ_License_ActiveActivation",
                table: "LicenseActivations",
                column: "LicenseId",
                unique: true,
                filter: "[Status] = 'Active'");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseActivations");

            migrationBuilder.DropTable(
                name: "Licenses");
        }
    }
}
