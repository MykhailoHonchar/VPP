using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccumulatorRampModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Devices");

            migrationBuilder.RenameColumn(
                name: "current",
                table: "Devices",
                newName: "targetCurrentKw");

            migrationBuilder.AddColumn<double>(
                name: "appliedCurrentKw",
                table: "Devices",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "RampRateKwPerHour",
                table: "AccumulatorModel",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            // A 0.0 ramp rate would freeze every existing accumulator's applied current
            // forever (max delta per tick is always 0). Backfill a real default —
            // reaches full rate in about 1 simulated hour — so existing seed data keeps
            // working after this migration instead of silently going inert.
            migrationBuilder.Sql(@"UPDATE ""AccumulatorModel"" SET ""RampRateKwPerHour"" = GREATEST(""MaxChargeKw"", ""MaxDischargeKw"")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "appliedCurrentKw",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RampRateKwPerHour",
                table: "AccumulatorModel");

            migrationBuilder.RenameColumn(
                name: "targetCurrentKw",
                table: "Devices",
                newName: "current");

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "Devices",
                type: "integer",
                nullable: true);
        }
    }
}
