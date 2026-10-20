using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace F1RaceLens.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class InitialSportsStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Circuits",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Locality = table.Column<string>(type: "text", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Circuits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Constructors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Constructors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    AdapterVersion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Drivers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Nationality = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Drivers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seasons",
                columns: table => new
                {
                    Year = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seasons", x => x.Year);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceKey = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RefreshAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Standings",
                columns: table => new
                {
                    DocumentId = table.Column<string>(type: "text", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ParticipantId = table.Column<string>(type: "text", nullable: false),
                    PositionText = table.Column<string>(type: "text", nullable: true),
                    Points = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Standings", x => new { x.DocumentId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_Standings_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Results",
                columns: table => new
                {
                    DocumentId = table.Column<string>(type: "text", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    DriverId = table.Column<string>(type: "text", nullable: false),
                    ConstructorId = table.Column<string>(type: "text", nullable: true),
                    CarNumber = table.Column<string>(type: "text", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: true),
                    PositionText = table.Column<string>(type: "text", nullable: true),
                    Grid = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Points = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Results", x => new { x.DocumentId, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_Results_Constructors_ConstructorId",
                        column: x => x.ConstructorId,
                        principalTable: "Constructors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Results_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Results_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Participations",
                columns: table => new
                {
                    Season = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<string>(type: "text", nullable: false),
                    ConstructorId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Participations", x => new { x.Season, x.DriverId, x.ConstructorId });
                    table.ForeignKey(
                        name: "FK_Participations_Constructors_ConstructorId",
                        column: x => x.ConstructorId,
                        principalTable: "Constructors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Participations_Drivers_DriverId",
                        column: x => x.DriverId,
                        principalTable: "Drivers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Participations_Seasons_Season",
                        column: x => x.Season,
                        principalTable: "Seasons",
                        principalColumn: "Year",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Races",
                columns: table => new
                {
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CircuitId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Races", x => new { x.Season, x.Round });
                    table.ForeignKey(
                        name: "FK_Races_Circuits_CircuitId",
                        column: x => x.CircuitId,
                        principalTable: "Circuits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Races_Seasons_Season",
                        column: x => x.Season,
                        principalTable: "Seasons",
                        principalColumn: "Year",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportPages",
                columns: table => new
                {
                    SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<string>(type: "text", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportPages", x => new { x.SnapshotId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_ImportPages_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportPages_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Season = table.Column<int>(type: "integer", nullable: false),
                    Round = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => new { x.Season, x.Round, x.Kind });
                    table.ForeignKey(
                        name: "FK_Sessions_Races_Season_Round",
                        columns: x => new { x.Season, x.Round },
                        principalTable: "Races",
                        principalColumns: new[] { "Season", "Round" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportPages_DocumentId",
                table: "ImportPages",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Participations_ConstructorId",
                table: "Participations",
                column: "ConstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_Participations_DriverId",
                table: "Participations",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Races_CircuitId",
                table: "Races",
                column: "CircuitId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_ConstructorId",
                table: "Results",
                column: "ConstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_DriverId",
                table: "Results",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_Results_Season_Round_Kind",
                table: "Results",
                columns: new[] { "Season", "Round", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_ResourceKey_FetchedAt",
                table: "Snapshots",
                columns: new[] { "ResourceKey", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_ResourceKey_PayloadHash",
                table: "Snapshots",
                columns: new[] { "ResourceKey", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Standings_Season_Round_Kind",
                table: "Standings",
                columns: new[] { "Season", "Round", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportPages");

            migrationBuilder.DropTable(
                name: "Participations");

            migrationBuilder.DropTable(
                name: "Results");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Standings");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "Constructors");

            migrationBuilder.DropTable(
                name: "Drivers");

            migrationBuilder.DropTable(
                name: "Races");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Circuits");

            migrationBuilder.DropTable(
                name: "Seasons");
        }
    }
}
