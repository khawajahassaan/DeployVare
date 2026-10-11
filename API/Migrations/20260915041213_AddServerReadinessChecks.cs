using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddServerReadinessChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerReadinessChecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationServerId = table.Column<int>(type: "int", nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CheckedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsReady = table.Column<bool>(type: "bit", nullable: false),
                    ReportJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_ServerReadinessChecks_ApplicationServerId",
                table: "ServerReadinessChecks",
                column: "ApplicationServerId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerReadinessChecks");
        }
    }
}
