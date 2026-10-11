using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace API.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactSourceMigrationsAndKeepPaths : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "KeepOnServerPaths",
                table: "ArtifactSources",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RunDatabaseMigrations",
                table: "ArtifactSources",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SqlScriptsArtifactName",
                table: "ArtifactSources",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeepOnServerPaths",
                table: "ArtifactSources");

            migrationBuilder.DropColumn(
                name: "RunDatabaseMigrations",
                table: "ArtifactSources");

            migrationBuilder.DropColumn(
                name: "SqlScriptsArtifactName",
                table: "ArtifactSources");
        }
    }
}
