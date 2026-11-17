using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Programs.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PriceAmountMinor",
                schema: "programs",
                table: "programs",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceModel",
                schema: "programs",
                table: "programs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Free");

            // Copy each program's current price (its highest price version) onto the program row.
            migrationBuilder.Sql(
                """
                UPDATE programs.programs AS p
                SET "PriceModel" = v."Model",
                    "PriceAmountMinor" = CASE WHEN v."Model" = 'Free' THEN NULL ELSE v."AmountMinor" END
                FROM (
                    SELECT DISTINCT ON ("ProgramId") "ProgramId", "Model", "AmountMinor"
                    FROM programs.price_versions
                    ORDER BY "ProgramId", "Version" DESC
                ) AS v
                WHERE v."ProgramId" = p."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceAmountMinor",
                schema: "programs",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "PriceModel",
                schema: "programs",
                table: "programs");
        }
    }
}
