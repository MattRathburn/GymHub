using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GH.Plan.DbManager.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanCreators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCreators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GHPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatorId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GHPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GHPlans_PlanCreators_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "PlanCreators",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PlanComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    GHPlanId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanComponents_GHPlans_GHPlanId",
                        column: x => x.GHPlanId,
                        principalTable: "GHPlans",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GHPlans_CreatorId",
                table: "GHPlans",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanComponents_GHPlanId",
                table: "PlanComponents",
                column: "GHPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanComponents");

            migrationBuilder.DropTable(
                name: "GHPlans");

            migrationBuilder.DropTable(
                name: "PlanCreators");
        }
    }
}
