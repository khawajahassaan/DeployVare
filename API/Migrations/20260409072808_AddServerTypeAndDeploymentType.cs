using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddServerTypeAndDeploymentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServerType",
                table: "ApplicationServers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'Api'");

            migrationBuilder.AddColumn<string>(
                name: "DeploymentType",
                table: "ApiDeployments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'Api'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ServerType",
                table: "ApplicationServers");

            migrationBuilder.DropColumn(
                name: "DeploymentType",
                table: "ApiDeployments");
        }
    }
}
