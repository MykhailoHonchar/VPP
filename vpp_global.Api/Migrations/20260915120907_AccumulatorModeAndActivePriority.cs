using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class AccumulatorModeAndActivePriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveAccPriority",
                table: "HomeSystems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Mode",
                table: "Devices",
                type: "integer",
                nullable: false,
                defaultValue: 0);   // AccumulatorMode.Idle
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveAccPriority",
                table: "HomeSystems");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Devices");
        }
    }
}
