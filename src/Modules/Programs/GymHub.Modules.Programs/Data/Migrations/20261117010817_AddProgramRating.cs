using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Programs.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramRating : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RatingCount",
                schema: "programs",
                table: "programs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RatingSum",
                schema: "programs",
                table: "programs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "RatingVersion",
                schema: "programs",
                table: "programs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatingCount",
                schema: "programs",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "RatingSum",
                schema: "programs",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "RatingVersion",
                schema: "programs",
                table: "programs");
        }
    }
}
