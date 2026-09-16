using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace vpp_global.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccumulatorModel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CapacityKwh = table.Column<double>(type: "double precision", nullable: false),
                    MaxChargeKw = table.Column<double>(type: "double precision", nullable: false),
                    MaxDischargeKw = table.Column<double>(type: "double precision", nullable: false),
                    RoundTripEfficiencyPercent = table.Column<double>(type: "double precision", nullable: false),
                    MaxDepthOfDischargePercent = table.Column<double>(type: "double precision", nullable: false),
                    RatedCycleLife = table.Column<int>(type: "integer", nullable: false),
                    Chemistry = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccumulatorModel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsumerModel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RatedPowerKw = table.Column<double>(type: "double precision", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    IsControllable = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumerModel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GeneratorModel",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RatedCapacityKw = table.Column<double>(type: "double precision", nullable: false),
                    EfficiencyPercent = table.Column<double>(type: "double precision", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeneratorModel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InverterModels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RatedPowerKw = table.Column<double>(type: "double precision", nullable: false),
                    MaxDcInputKw = table.Column<double>(type: "double precision", nullable: false),
                    EfficiencyPercent = table.Column<double>(type: "double precision", nullable: false),
                    IsHybrid = table.Column<bool>(type: "boolean", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Manufacturer = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InverterModels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PowerReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<int>(type: "integer", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PowerKw = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerReadings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GridNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    RegionId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridNodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridNodes_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HomeSystems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    GridNodeId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeSystems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HomeSystems_GridNodes_GridNodeId",
                        column: x => x.GridNodeId,
                        principalTable: "GridNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    HomeSystemId = table.Column<int>(type: "integer", nullable: false),
                    Discriminator = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    CurrentSocPercent = table.Column<double>(type: "double precision", nullable: true),
                    Accumulator_ModelId = table.Column<int>(type: "integer", nullable: true),
                    Chemistry = table.Column<string>(type: "text", nullable: true),
                    IsConnected = table.Column<bool>(type: "boolean", nullable: true),
                    V2GCapable = table.Column<bool>(type: "boolean", nullable: true),
                    LicensePlate = table.Column<string>(type: "text", nullable: true),
                    ModelId = table.Column<int>(type: "integer", nullable: true),
                    Generator_ModelId = table.Column<int>(type: "integer", nullable: true),
                    TotalPanelAreaM2 = table.Column<double>(type: "double precision", nullable: true),
                    TiltDegrees = table.Column<int>(type: "integer", nullable: true),
                    AzimuthDegrees = table.Column<int>(type: "integer", nullable: true),
                    RotorDiameterM = table.Column<double>(type: "double precision", nullable: true),
                    CutInWindSpeedMs = table.Column<double>(type: "double precision", nullable: true),
                    Inverter_ModelId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devices_AccumulatorModel_Accumulator_ModelId",
                        column: x => x.Accumulator_ModelId,
                        principalTable: "AccumulatorModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Devices_ConsumerModel_ModelId",
                        column: x => x.ModelId,
                        principalTable: "ConsumerModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Devices_GeneratorModel_Generator_ModelId",
                        column: x => x.Generator_ModelId,
                        principalTable: "GeneratorModel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Devices_HomeSystems_HomeSystemId",
                        column: x => x.HomeSystemId,
                        principalTable: "HomeSystems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Devices_InverterModels_Inverter_ModelId",
                        column: x => x.Inverter_ModelId,
                        principalTable: "InverterModels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PowerSpectra",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DeviceId = table.Column<int>(type: "integer", nullable: false),
                    ReferenceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MeanPowerKw = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PowerSpectra", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PowerSpectra_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Simulations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MeanKw = table.Column<double>(type: "double precision", nullable: false),
                    AmplitudeKw = table.Column<double>(type: "double precision", nullable: false),
                    PeriodHours = table.Column<double>(type: "double precision", nullable: false),
                    NoiseStdDevKw = table.Column<double>(type: "double precision", nullable: false),
                    PhaseShift = table.Column<double>(type: "double precision", nullable: false),
                    DeviceId = table.Column<int>(type: "integer", nullable: true),
                    GridNodeId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Simulations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Simulations_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Simulations_GridNodes_GridNodeId",
                        column: x => x.GridNodeId,
                        principalTable: "GridNodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SpectralComponents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PowerSpectrumId = table.Column<int>(type: "integer", nullable: false),
                    PeriodHours = table.Column<double>(type: "double precision", nullable: false),
                    AmplitudeKw = table.Column<double>(type: "double precision", nullable: false),
                    PhaseRadians = table.Column<double>(type: "double precision", nullable: false),
                    SignificanceScore = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpectralComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpectralComponents_PowerSpectra_PowerSpectrumId",
                        column: x => x.PowerSpectrumId,
                        principalTable: "PowerSpectra",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Accumulator_ModelId",
                table: "Devices",
                column: "Accumulator_ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Generator_ModelId",
                table: "Devices",
                column: "Generator_ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_HomeSystemId",
                table: "Devices",
                column: "HomeSystemId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_Inverter_ModelId",
                table: "Devices",
                column: "Inverter_ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_Devices_ModelId",
                table: "Devices",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_GridNodes_RegionId",
                table: "GridNodes",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_HomeSystems_GridNodeId",
                table: "HomeSystems",
                column: "GridNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_PowerSpectra_DeviceId",
                table: "PowerSpectra",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Simulations_DeviceId",
                table: "Simulations",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_Simulations_GridNodeId",
                table: "Simulations",
                column: "GridNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_SpectralComponents_PowerSpectrumId",
                table: "SpectralComponents",
                column: "PowerSpectrumId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PowerReadings");

            migrationBuilder.DropTable(
                name: "Simulations");

            migrationBuilder.DropTable(
                name: "SpectralComponents");

            migrationBuilder.DropTable(
                name: "PowerSpectra");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "AccumulatorModel");

            migrationBuilder.DropTable(
                name: "ConsumerModel");

            migrationBuilder.DropTable(
                name: "GeneratorModel");

            migrationBuilder.DropTable(
                name: "HomeSystems");

            migrationBuilder.DropTable(
                name: "InverterModels");

            migrationBuilder.DropTable(
                name: "GridNodes");

            migrationBuilder.DropTable(
                name: "Regions");
        }
    }
}
