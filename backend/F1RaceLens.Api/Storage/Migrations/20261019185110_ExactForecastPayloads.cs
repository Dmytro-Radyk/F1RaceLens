using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace F1RaceLens.Api.Storage.Migrations
{
    /// <inheritdoc />
    public partial class ExactForecastPayloads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Actual",
                table: "ForecastScores",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "Output",
                table: "ForecastRuns",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");

            migrationBuilder.AlterColumn<string>(
                name: "Inputs",
                table: "ForecastRuns",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "jsonb");
            // JSONB reordered early development payloads. Hash their preserved text representation once.
            migrationBuilder.Sql("""
                UPDATE "ForecastRuns" SET
                  "InputHash" = encode(sha256(convert_to("Inputs", 'UTF8')), 'hex'),
                  "OutputHash" = encode(sha256(convert_to("Output", 'UTF8')), 'hex');
                UPDATE "ForecastScores" SET "ActualHash" = encode(sha256(convert_to("Actual", 'UTF8')), 'hex');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Actual",
                table: "ForecastScores",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Output",
                table: "ForecastRuns",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Inputs",
                table: "ForecastRuns",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}

