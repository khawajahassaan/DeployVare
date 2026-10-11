using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentLicensingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LicenseId",
                table: "ApiDeployments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicensingMode",
                table: "ApiDeployments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValueSql: "'WithoutLicense'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LicenseId",
                table: "ApiDeployments");

            migrationBuilder.DropColumn(
                name: "LicensingMode",
                table: "ApiDeployments");
        }
    }
}
