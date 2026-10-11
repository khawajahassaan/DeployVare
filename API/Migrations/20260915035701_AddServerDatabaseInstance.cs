using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddServerDatabaseInstance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DatabaseInstanceId",
                table: "ApplicationServers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationServers_DatabaseInstanceId",
                table: "ApplicationServers",
                column: "DatabaseInstanceId");

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationServers_DatabaseInstances_DatabaseInstanceId",
                table: "ApplicationServers",
                column: "DatabaseInstanceId",
                principalTable: "DatabaseInstances",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationServers_DatabaseInstances_DatabaseInstanceId",
                table: "ApplicationServers");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationServers_DatabaseInstanceId",
                table: "ApplicationServers");

            migrationBuilder.DropColumn(
                name: "DatabaseInstanceId",
                table: "ApplicationServers");
        }
    }
}
