using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Programs.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProgramTrialDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TrialDays",
                schema: "programs",
                table: "price_versions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TrialDays",
                schema: "programs",
                table: "price_versions");
        }
    }
}
