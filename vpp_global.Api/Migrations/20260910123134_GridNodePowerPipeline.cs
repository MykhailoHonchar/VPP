using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class GridNodePowerPipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Patched to be idempotent: this migration was never recorded as applied in
            // __EFMigrationsHistory, but part of it (PowerSpectra.GridNodeId) already
            // existed in the live DB from an earlier out-of-band change. Every operation
            // below is guarded so it's safe to run regardless of what already happened.
            migrationBuilder.Sql(@"ALTER TABLE ""PowerSpectra"" DROP CONSTRAINT IF EXISTS ""FK_PowerSpectra_Devices_DeviceId"";");

            migrationBuilder.AlterColumn<int>(
                name: "DeviceId",
                table: "PowerSpectra",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.Sql(@"ALTER TABLE ""PowerSpectra"" ADD COLUMN IF NOT EXISTS ""GridNodeId"" integer;");

            migrationBuilder.AlterColumn<int>(
                name: "DeviceId",
                table: "PowerReadings",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.Sql(@"ALTER TABLE ""PowerReadings"" ADD COLUMN IF NOT EXISTS ""GridNodeId"" integer;");

            migrationBuilder.Sql(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_PowerSpectra_GridNodeId"" ON ""PowerSpectra"" (""GridNodeId"");");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_PowerSpectra_Devices_DeviceId') THEN
                        ALTER TABLE ""PowerSpectra"" ADD CONSTRAINT ""FK_PowerSpectra_Devices_DeviceId"" FOREIGN KEY (""DeviceId"") REFERENCES ""Devices"" (""Id"");
                    END IF;
                END $$;");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_PowerSpectra_GridNodes_GridNodeId') THEN
                        ALTER TABLE ""PowerSpectra"" ADD CONSTRAINT ""FK_PowerSpectra_GridNodes_GridNodeId"" FOREIGN KEY (""GridNodeId"") REFERENCES ""GridNodes"" (""Id"");
                    END IF;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PowerSpectra_Devices_DeviceId",
                table: "PowerSpectra");

            migrationBuilder.DropForeignKey(
                name: "FK_PowerSpectra_GridNodes_GridNodeId",
                table: "PowerSpectra");

            migrationBuilder.DropIndex(
                name: "IX_PowerSpectra_GridNodeId",
                table: "PowerSpectra");

            migrationBuilder.DropColumn(
                name: "GridNodeId",
                table: "PowerSpectra");

            migrationBuilder.DropColumn(
                name: "GridNodeId",
                table: "PowerReadings");

            migrationBuilder.AlterColumn<int>(
                name: "DeviceId",
                table: "PowerSpectra",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DeviceId",
                table: "PowerReadings",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PowerSpectra_Devices_DeviceId",
                table: "PowerSpectra",
                column: "DeviceId",
                principalTable: "Devices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
