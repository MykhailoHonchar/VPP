using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenamePropertiesToPascalCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "lowPrice",
                table: "HomeSystems",
                newName: "LowPrice");

            migrationBuilder.RenameColumn(
                name: "highPrice",
                table: "HomeSystems",
                newName: "HighPrice");

            migrationBuilder.RenameColumn(
                name: "targetCurrentKw",
                table: "Devices",
                newName: "TargetCurrentKw");

            migrationBuilder.RenameColumn(
                name: "onGrid",
                table: "Devices",
                newName: "OnGrid");

            migrationBuilder.RenameColumn(
                name: "onAcc",
                table: "Devices",
                newName: "OnAcc");

            migrationBuilder.RenameColumn(
                name: "minKWH",
                table: "Devices",
                newName: "MinKWH");

            migrationBuilder.RenameColumn(
                name: "maxKWH",
                table: "Devices",
                newName: "MaxKWH");

            migrationBuilder.RenameColumn(
                name: "lowKWH",
                table: "Devices",
                newName: "LowKWH");

            migrationBuilder.RenameColumn(
                name: "appliedCurrentKw",
                table: "Devices",
                newName: "AppliedCurrentKw");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "LowPrice",
                table: "HomeSystems",
                newName: "lowPrice");

            migrationBuilder.RenameColumn(
                name: "HighPrice",
                table: "HomeSystems",
                newName: "highPrice");

            migrationBuilder.RenameColumn(
                name: "TargetCurrentKw",
                table: "Devices",
                newName: "targetCurrentKw");

            migrationBuilder.RenameColumn(
                name: "OnGrid",
                table: "Devices",
                newName: "onGrid");

            migrationBuilder.RenameColumn(
                name: "OnAcc",
                table: "Devices",
                newName: "onAcc");

            migrationBuilder.RenameColumn(
                name: "MinKWH",
                table: "Devices",
                newName: "minKWH");

            migrationBuilder.RenameColumn(
                name: "MaxKWH",
                table: "Devices",
                newName: "maxKWH");

            migrationBuilder.RenameColumn(
                name: "LowKWH",
                table: "Devices",
                newName: "lowKWH");

            migrationBuilder.RenameColumn(
                name: "AppliedCurrentKw",
                table: "Devices",
                newName: "appliedCurrentKw");
        }
    }
}
