-- Wipes every table and rebuilds exactly one complete, working home system:
-- Region -> GridNode -> HomeSystem -> Inverter, 1 solar panel, 3 batteries
-- (priorities 0/1/2, matching HomeSysLogic.cs's current maxPriority = 2),
-- 1 consumer, plus Simulation rows for solar/consumer/grid price.
--
-- RESTART IDENTITY resets every auto-increment counter to 1, so this new
-- home system, grid node, and region all come back as Id = 1 — matching
-- every hardcoded "/homesystem/1", "/gridnode/1" reference already in the
-- frontend and in past testing.
--
-- Review before running; run yourself (not something I'll execute).

BEGIN;

TRUNCATE TABLE
    "SpectralComponents",
    "PowerSpectra",
    "PowerReadings",
    "Simulations",
    "Devices",
    "HomeSystems",
    "GridNodes",
    "Regions",
    "AccumulatorModel",
    "ConsumerModel",
    "GeneratorModel",
    "InverterModels"
RESTART IDENTITY CASCADE;

WITH
new_region AS (
    INSERT INTO "Regions" ("Name") VALUES ('Test Region') RETURNING "Id"
),
new_grid_node AS (
    INSERT INTO "GridNodes" ("Name", "RegionId")
    SELECT 'Test Grid Node', "Id" FROM new_region
    RETURNING "Id"
),
new_home_system AS (
    INSERT INTO "HomeSystems" ("Name", "GridNodeId", "lowPrice", "highPrice")
    SELECT 'Test Home System', "Id", 0.15, 0.35 FROM new_grid_node
    RETURNING "Id"
),

-- Models
new_inverter_model AS (
    INSERT INTO "InverterModels" ("Name", "Manufacturer", "RatedPowerKw", "MaxDcInputKw", "EfficiencyPercent", "IsHybrid")
    VALUES ('Standard Hybrid Inverter', 'InverTech', 5.0, 6.0, 97, true)
    RETURNING "Id"
),
new_generator_model AS (
    INSERT INTO "GeneratorModel" ("Name", "Manufacturer", "RatedCapacityKw", "EfficiencyPercent")
    VALUES ('Standard Solar Array', 'SunTech', 6.0, 20)
    RETURNING "Id"
),
new_accumulator_model AS (
    INSERT INTO "AccumulatorModel"
        ("Name", "Manufacturer", "CapacityKwh", "MaxChargeKw", "MaxDischargeKw",
         "RoundTripEfficiencyPercent", "MaxDepthOfDischargePercent", "RatedCycleLife",
         "Chemistry", "RampRateKwPerHour")
    VALUES ('Standard Home Battery', 'PowerCell', 100, 5, 5, 90, 90, 6000, 'LiFePO4', 5)
    RETURNING "Id"
),
new_consumer_model AS (
    INSERT INTO "ConsumerModel" ("Name", "Manufacturer", "RatedPowerKw", "Category", "IsControllable")
    VALUES ('Household Load', 'Generic', 3.0, 'Mixed', false)
    RETURNING "Id"
),

-- Devices
new_inverter AS (
    INSERT INTO "Devices"
        ("Discriminator", "HomeSystemId", "Name", "Inverter_ModelId",
         "InverterCurrent", "InverterMode", "MaxDCInput", "MaxOutputPower",
         "ac2dcEfficiency", "dc2acEfficiency", "onAcc", "onGrid")
    SELECT 'Inverter', hs."Id", 'Inverter 1', im."Id",
           0, 0, 6.0, 5.0,
           0.97, 0.97, true, true
    FROM new_home_system hs, new_inverter_model im
    RETURNING "Id"
),
new_solar AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Generator_ModelId",
                            "TotalPanelAreaM2", "TiltDegrees", "AzimuthDegrees")
    SELECT 'SolarPowerPlant', hs."Id", 'Solar Array 1', gm."Id", 30, 30, 180
    FROM new_home_system hs, new_generator_model gm
    RETURNING "Id"
),
new_battery_1 AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Accumulator_ModelId",
                            "CapacityKWH", "CurrentChargeKWH", "LastTickAt", "Mode", "Priority",
                            "appliedCurrentKw", "targetCurrentKw", "lowKWH", "maxKWH", "minKWH", "Chemistry")
    SELECT 'Battery', hs."Id", 'Battery 1', am."Id", 100, 100, NULL, 4, 0, 0, 0, 20, 95, 5, 'LiFePO4'
    FROM new_home_system hs, new_accumulator_model am
    RETURNING "Id"
),
new_battery_2 AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Accumulator_ModelId",
                            "CapacityKWH", "CurrentChargeKWH", "LastTickAt", "Mode", "Priority",
                            "appliedCurrentKw", "targetCurrentKw", "lowKWH", "maxKWH", "minKWH", "Chemistry")
    SELECT 'Battery', hs."Id", 'Battery 2', am."Id", 100, 100, NULL, 4, 1, 0, 0, 20, 95, 5, 'LiFePO4'
    FROM new_home_system hs, new_accumulator_model am
    RETURNING "Id"
),
new_battery_3 AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Accumulator_ModelId",
                            "CapacityKWH", "CurrentChargeKWH", "LastTickAt", "Mode", "Priority",
                            "appliedCurrentKw", "targetCurrentKw", "lowKWH", "maxKWH", "minKWH", "Chemistry")
    SELECT 'Battery', hs."Id", 'Battery 3', am."Id", 100, 100, NULL, 4, 2, 0, 0, 20, 95, 5, 'LiFePO4'
    FROM new_home_system hs, new_accumulator_model am
    RETURNING "Id"
),
new_consumer AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "ModelId")
    SELECT 'Consumer', hs."Id", 'Household Consumer', cm."Id"
    FROM new_home_system hs, new_consumer_model cm
    RETURNING "Id"
),

-- Simulations — solar: daily generation curve + slow weekly drift
-- Aggressive values (see update-simulations-aggressive.sql for the reasoning): peaks at
-- 6kW at noon, 0 at night, so a future full reset doesn't regress to the old, weak curve.
sim_solar_daily AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 3, 3, 24, -6, 0.5, false FROM new_solar
),
sim_solar_weekly AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 0, 0.7, 168, 0, 0.15, false FROM new_solar
),

-- Simulations — consumer: daily usage curve + slow weekly drift
-- Peaks at 8kW at night (already 180 degrees out of phase from solar) so demand
-- saturates the battery's 5kW discharge cap for hours at a time instead of barely
-- denting it.
sim_consumer_daily AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 4.5, 3.5, 24, 6, 0.4, false FROM new_consumer
),
sim_consumer_weekly AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 0, 0.5, 168, 12, 0.15, false FROM new_consumer
),

-- Simulations — grid price: daily cycle + sharper peak component + weekly drift
sim_price_daily AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT NULL, "Id", 0.20, 0.08, 24, -4, 0.02, false FROM new_grid_node
),
sim_price_peak AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT NULL, "Id", 0, 0.05, 12, -2, 0.01, false FROM new_grid_node
),
sim_price_weekly AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT NULL, "Id", 0, 0.03, 168, 0, 0.01, false FROM new_grid_node
)

SELECT
    (SELECT "Id" FROM new_region)       AS region_id,
    (SELECT "Id" FROM new_grid_node)    AS grid_node_id,
    (SELECT "Id" FROM new_home_system)  AS home_system_id,
    (SELECT "Id" FROM new_inverter)     AS inverter_device_id,
    (SELECT "Id" FROM new_solar)        AS solar_device_id,
    (SELECT "Id" FROM new_battery_1)    AS battery_1_id,
    (SELECT "Id" FROM new_battery_2)    AS battery_2_id,
    (SELECT "Id" FROM new_battery_3)    AS battery_3_id,
    (SELECT "Id" FROM new_consumer)     AS consumer_device_id;

COMMIT;
