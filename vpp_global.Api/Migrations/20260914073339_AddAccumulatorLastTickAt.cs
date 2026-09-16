using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccumulatorLastTickAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsConnected",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "V2GCapable",
                table: "Devices");

            migrationBuilder.RenameColumn(
                name: "MinDCInputDischarge",
                table: "Devices",
                newName: "minKWH");

            migrationBuilder.RenameColumn(
                name: "Inverter_Mode",
                table: "Devices",
                newName: "InverterMode");

            migrationBuilder.RenameColumn(
                name: "CurrentSocPercent",
                table: "Devices",
                newName: "maxKWH");

            migrationBuilder.AddColumn<double>(
                name: "highPrice",
                table: "HomeSystems",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "lowPrice",
                table: "HomeSystems",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "CapacityKWH",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "CurrentChargeKWH",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastTickAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ac2dcEfficiency",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "dc2acEfficiency",
                table: "Devices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "lowKWH",
                table: "Devices",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "highPrice",
                table: "HomeSystems");

            migrationBuilder.DropColumn(
                name: "lowPrice",
                table: "HomeSystems");

            migrationBuilder.DropColumn(
                name: "CapacityKWH",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CurrentChargeKWH",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LastTickAt",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "ac2dcEfficiency",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "dc2acEfficiency",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "lowKWH",
                table: "Devices");

            migrationBuilder.RenameColumn(
                name: "minKWH",
                table: "Devices",
                newName: "MinDCInputDischarge");

            migrationBuilder.RenameColumn(
                name: "maxKWH",
                table: "Devices",
                newName: "CurrentSocPercent");

            migrationBuilder.RenameColumn(
                name: "InverterMode",
                table: "Devices",
                newName: "Inverter_Mode");

            migrationBuilder.AddColumn<bool>(
                name: "IsConnected",
                table: "Devices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "V2GCapable",
                table: "Devices",
                type: "boolean",
                nullable: true);
        }
    }
}
