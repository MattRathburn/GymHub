using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GH.Program.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreateForPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProgramCreators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramCreators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GHPrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CreatorId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GHPrograms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GHPrograms_ProgramCreators_CreatorId",
                        column: x => x.CreatorId,
                        principalTable: "ProgramCreators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProgramComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    GHProgramId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramComponents_GHPrograms_GHProgramId",
                        column: x => x.GHProgramId,
                        principalTable: "GHPrograms",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GHPrograms_CreatorId",
                table: "GHPrograms",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProgramComponents_GHProgramId",
                table: "ProgramComponents",
                column: "GHProgramId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProgramComponents");

            migrationBuilder.DropTable(
                name: "GHPrograms");

            migrationBuilder.DropTable(
                name: "ProgramCreators");
        }
    }
}
