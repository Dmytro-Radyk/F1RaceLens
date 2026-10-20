using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1RaceLens.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ExactSourcePoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Points",
                table: "Standings",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "Points",
                table: "Results",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Results_Races_Season_Round",
                table: "Results",
                columns: new[] { "Season", "Round" },
                principalTable: "Races",
                principalColumns: new[] { "Season", "Round" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Standings_Seasons_Season",
                table: "Standings",
                column: "Season",
                principalTable: "Seasons",
                principalColumn: "Year",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Results_Races_Season_Round",
                table: "Results");

            migrationBuilder.DropForeignKey(
                name: "FK_Standings_Seasons_Season",
                table: "Standings");

            migrationBuilder.AlterColumn<decimal>(
                name: "Points",
                table: "Standings",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<decimal>(
                name: "Points",
                table: "Results",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);
        }
    }
}
