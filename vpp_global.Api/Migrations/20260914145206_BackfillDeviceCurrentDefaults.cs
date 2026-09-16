using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class BackfillDeviceCurrentDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Devices.InverterCurrent/current were added as nullable columns (required
            // for a TPH shared table, since other discriminator types legitimately have
            // no value there), but both are non-nullable `double` in C# — existing rows
            // would read back as NULL and throw the next time EF materializes them.
            // Backfill only the rows where the column is actually meaningful.
            migrationBuilder.Sql(@"UPDATE ""Devices"" SET ""InverterCurrent"" = 0 WHERE ""Discriminator"" = 'Inverter' AND ""InverterCurrent"" IS NULL;");
            migrationBuilder.Sql(@"UPDATE ""Devices"" SET ""current"" = 0 WHERE ""Discriminator"" IN ('Battery', 'EV') AND ""current"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
