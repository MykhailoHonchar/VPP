using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class HomeSystemDeviceNavigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Inverter_Mode",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MaxDCInput",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MaxOutputPower",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "MinDCInputDischarge",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "onAcc",
                table: "Devices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "onGrid",
                table: "Devices",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Inverter_Mode",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MaxDCInput",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MaxOutputPower",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MinDCInputDischarge",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "onAcc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "onGrid",
                table: "Devices");
        }
    }
}
