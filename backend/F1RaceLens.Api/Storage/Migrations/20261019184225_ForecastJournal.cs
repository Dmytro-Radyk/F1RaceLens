using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1RaceLens.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ForecastJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForecastRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Engine = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    ThroughRound = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    Inputs = table.Column<string>(type: "jsonb", nullable: false),
                    OutputHash = table.Column<string>(type: "text", nullable: false),
                    Output = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForecastSeals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Engine = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    ThroughRound = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FrozenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastSeals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastSeals_ForecastRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "ForecastRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForecastScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SealId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActualHash = table.Column<string>(type: "text", nullable: false),
                    Actual = table.Column<string>(type: "jsonb", nullable: false),
                    ResultUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RankMae = table.Column<double>(type: "double precision", nullable: false),
                    WinnerBrier = table.Column<double>(type: "double precision", nullable: false),
                    WinnerHit = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastScores_ForecastSeals_SealId",
                        column: x => x.SealId,
                        principalTable: "ForecastSeals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastRuns_Engine_OutputHash_InputHash",
                table: "ForecastRuns",
                columns: new[] { "Engine", "OutputHash", "InputHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForecastScores_SealId_ActualHash",
                table: "ForecastScores",
                columns: new[] { "SealId", "ActualHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSeals_Engine_Version_Season_Round_ThroughRound",
                table: "ForecastSeals",
                columns: new[] { "Engine", "Version", "Season", "Round", "ThroughRound" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForecastSeals_RunId",
                table: "ForecastSeals",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForecastScores");

            migrationBuilder.DropTable(
                name: "ForecastSeals");

            migrationBuilder.DropTable(
                name: "ForecastRuns");
        }
    }
}
