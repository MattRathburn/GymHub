using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GymHub.Modules.Programs.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "programs");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "programs",
                schema: "programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainerId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    CoverObjectKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFree = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_programs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "media",
                schema: "programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Caption = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media", x => x.Id);
                    table.ForeignKey(
                        name: "FK_media_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "programs",
                        principalTable: "programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "price_versions",
                schema: "programs",
                columns: table => new
                {
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Interval = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Model = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_versions", x => new { x.ProgramId, x.Version });
                    table.ForeignKey(
                        name: "FK_price_versions_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "programs",
                        principalTable: "programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "weeks",
                schema: "programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReleasesOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReleaseAnnouncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProgramId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_weeks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_weeks_programs_ProgramId",
                        column: x => x.ProgramId,
                        principalSchema: "programs",
                        principalTable: "programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "days",
                schema: "programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsRestDay = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    WeekId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_days", x => x.Id);
                    table.ForeignKey(
                        name: "FK_days_weeks_WeekId",
                        column: x => x.WeekId,
                        principalSchema: "programs",
                        principalTable: "weeks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "workouts",
                schema: "programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    DayId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_workouts_days_DayId",
                        column: x => x.DayId,
                        principalSchema: "programs",
                        principalTable: "days",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "exercises",
                schema: "programs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    WorkoutId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sets = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_exercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_exercises_workouts_WorkoutId",
                        column: x => x.WorkoutId,
                        principalSchema: "programs",
                        principalTable: "workouts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_days_WeekId_Number",
                schema: "programs",
                table: "days",
                columns: new[] { "WeekId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_exercises_WorkoutId",
                schema: "programs",
                table: "exercises",
                column: "WorkoutId");

            migrationBuilder.CreateIndex(
                name: "IX_media_ProgramId",
                schema: "programs",
                table: "media",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_media_TargetKind_TargetId",
                schema: "programs",
                table: "media",
                columns: new[] { "TargetKind", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_ProcessedAt_OccurredAt",
                schema: "programs",
                table: "outbox_messages",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_programs_Status",
                schema: "programs",
                table: "programs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_programs_TrainerId",
                schema: "programs",
                table: "programs",
                column: "TrainerId");

            migrationBuilder.CreateIndex(
                name: "IX_weeks_ProgramId",
                schema: "programs",
                table: "weeks",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_weeks_ReleasesOn_ReleaseAnnouncedAt",
                schema: "programs",
                table: "weeks",
                columns: new[] { "ReleasesOn", "ReleaseAnnouncedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_workouts_DayId",
                schema: "programs",
                table: "workouts",
                column: "DayId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercises",
                schema: "programs");

            migrationBuilder.DropTable(
                name: "media",
                schema: "programs");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "programs");

            migrationBuilder.DropTable(
                name: "price_versions",
                schema: "programs");

            migrationBuilder.DropTable(
                name: "workouts",
                schema: "programs");

            migrationBuilder.DropTable(
                name: "days",
                schema: "programs");

            migrationBuilder.DropTable(
                name: "weeks",
                schema: "programs");

            migrationBuilder.DropTable(
                name: "programs",
                schema: "programs");
        }
    }
}
