using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInverterCurrent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded with IF NOT EXISTS — same rationale as GridNodePowerPipeline's
            // patch earlier: the live DB has drifted from migration history (columns
            // added out-of-band), so these must be safe to run whether or not they
            // already happened.
            migrationBuilder.Sql(@"ALTER TABLE ""Simulations"" ADD COLUMN IF NOT EXISTS ""AllowNegative"" boolean NOT NULL DEFAULT FALSE;");
            migrationBuilder.Sql(@"ALTER TABLE ""Devices"" ADD COLUMN IF NOT EXISTS ""InverterCurrent"" double precision;");
            migrationBuilder.Sql(@"ALTER TABLE ""Devices"" ADD COLUMN IF NOT EXISTS ""current"" double precision;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowNegative",
                table: "Simulations");

            migrationBuilder.DropColumn(
                name: "InverterCurrent",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "current",
                table: "Devices");
        }
    }
}
