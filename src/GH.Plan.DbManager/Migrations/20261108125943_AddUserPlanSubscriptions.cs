using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GH.Plan.DbManager.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPlanSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPlanSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlanSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPlanSubscriptions_GHPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "GHPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPlanSubscriptions_PlanId",
                table: "UserPlanSubscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlanSubscriptions_UserId_PlanId",
                table: "UserPlanSubscriptions",
                columns: new[] { "UserId", "PlanId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPlanSubscriptions");
        }
    }
}
