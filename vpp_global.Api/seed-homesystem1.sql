-- Adds to HomeSystemId = 1: 1 solar panel array, 3 batteries, 1 consumer —
-- each device model created fresh (not reusing any existing model row).
-- Each of {solar, consumer, grid price} gets multiple summed Simulation
-- components (a daily cycle + a slower weekly drift + noise) rather than one
-- flat sine, matching the "complex simulation" pattern already used elsewhere
-- in this project. Batteries intentionally get none — see note above.
--
-- All "Id" columns are IDENTITY (Postgres auto-generates them) — this script
-- never hardcodes an Id, it chains newly-generated ones via RETURNING/CTEs.
-- Review before running; run yourself (not something I'll execute).

BEGIN;

WITH
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
new_solar AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Generator_ModelId",
                            "TotalPanelAreaM2", "TiltDegrees", "AzimuthDegrees")
    SELECT 'SolarPowerPlant', 1, 'Solar Array 1', "Id", 30, 30, 180
    FROM new_generator_model
    RETURNING "Id"
),
new_battery_1 AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Accumulator_ModelId",
                            "CapacityKWH", "CurrentChargeKWH", "LastTickAt", "Mode", "Priority",
                            "appliedCurrentKw", "targetCurrentKw", "lowKWH", "maxKWH", "minKWH", "Chemistry")
    SELECT 'Battery', 1, 'Battery 1', "Id", 100, 100, NULL, 4, 0, 0, 0, 20, 95, 5, 'LiFePO4'
    FROM new_accumulator_model
    RETURNING "Id"
),
new_battery_2 AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Accumulator_ModelId",
                            "CapacityKWH", "CurrentChargeKWH", "LastTickAt", "Mode", "Priority",
                            "appliedCurrentKw", "targetCurrentKw", "lowKWH", "maxKWH", "minKWH", "Chemistry")
    SELECT 'Battery', 1, 'Battery 2', "Id", 100, 100, NULL, 4, 1, 0, 0, 20, 95, 5, 'LiFePO4'
    FROM new_accumulator_model
    RETURNING "Id"
),
new_battery_3 AS (
    -- Priority 2 — currently unreachable by the automatic switch until maxPriority
    -- stops being hardcoded to 1 in HomeSysLogic.cs. Still fully visible/queryable.
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "Accumulator_ModelId",
                            "CapacityKWH", "CurrentChargeKWH", "LastTickAt", "Mode", "Priority",
                            "appliedCurrentKw", "targetCurrentKw", "lowKWH", "maxKWH", "minKWH", "Chemistry")
    SELECT 'Battery', 1, 'Battery 3', "Id", 100, 100, NULL, 4, 2, 0, 0, 20, 95, 5, 'LiFePO4'
    FROM new_accumulator_model
    RETURNING "Id"
),
new_consumer AS (
    INSERT INTO "Devices" ("Discriminator", "HomeSystemId", "Name", "ModelId")
    SELECT 'Consumer', 1, 'Household Consumer', "Id"
    FROM new_consumer_model
    RETURNING "Id"
),

-- Simulations — solar: daily generation curve + slow weekly drift
sim_solar_daily AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 2.5, 2.5, 24, -6, 0.3, false FROM new_solar
),
sim_solar_weekly AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 0, 0.5, 168, 0, 0.1, false FROM new_solar
),

-- Simulations — consumer: daily usage curve + slow weekly drift
sim_consumer_daily AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 1.2, 0.8, 24, 6, 0.2, false FROM new_consumer
),
sim_consumer_weekly AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    SELECT "Id", NULL, 0, 0.3, 168, 12, 0.1, false FROM new_consumer
),

-- Simulations — grid price (GridNodeId = 1, DeviceId NULL): daily cycle + a
-- sharper mid-day/evening peak component + slow weekly drift
sim_price_daily AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    VALUES (NULL, 1, 0.20, 0.08, 24, -4, 0.02, false)
),
sim_price_peak AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    VALUES (NULL, 1, 0, 0.05, 12, -2, 0.01, false)
),
sim_price_weekly AS (
    INSERT INTO "Simulations" ("DeviceId", "GridNodeId", "MeanKw", "AmplitudeKw", "PeriodHours", "PhaseShift", "NoiseStdDevKw", "AllowNegative")
    VALUES (NULL, 1, 0, 0.03, 168, 0, 0.01, false)
)

SELECT
    (SELECT "Id" FROM new_solar)     AS solar_device_id,
    (SELECT "Id" FROM new_battery_1) AS battery_1_id,
    (SELECT "Id" FROM new_battery_2) AS battery_2_id,
    (SELECT "Id" FROM new_battery_3) AS battery_3_id,
    (SELECT "Id" FROM new_consumer)  AS consumer_device_id;

COMMIT;
