using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Programs.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogPriceSort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriceCurrency",
                schema: "programs",
                table: "programs",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PriceInterval",
                schema: "programs",
                table: "programs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Copy each paid program's current currency and interval (its highest price version) onto the program row.
            migrationBuilder.Sql(
                """
                UPDATE programs.programs AS p
                SET "PriceCurrency" = v."Currency",
                    "PriceInterval" = v."Interval"
                FROM (
                    SELECT DISTINCT ON ("ProgramId") "ProgramId", "Model", "Currency", "Interval"
                    FROM programs.price_versions
                    ORDER BY "ProgramId", "Version" DESC
                ) AS v
                WHERE v."ProgramId" = p."Id" AND v."Model" <> 'Free';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceCurrency",
                schema: "programs",
                table: "programs");

            migrationBuilder.DropColumn(
                name: "PriceInterval",
                schema: "programs",
                table: "programs");
        }
    }
}
