-- Replaces the solar/consumer Simulation curve parameters with more aggressive ones so
-- the batteries actually swing between charging/draining/full/empty instead of sitting
-- around 85-95% SoC forever.
--
-- Why the old values barely moved the batteries:
--   - Solar daily: Mean=2.5, Amplitude=2.5 -> peaks at 5kW at noon, 0 at night.
--   - Consumer daily: Mean=1.2, Amplitude=0.8 -> only ranges 0.4-2.0kW.
--   Even at night (solar=0), demand never exceeded ~2kW, so a single 100kWh battery
--   (discharging at its 5kW cap) would take ~50 real-world-equivalent hours to drain --
--   and with 3 batteries stacked by priority, ~150 hours before battery #1 ever needed
--   to hand off, let alone hit Empty and trigger a Buy.
--
-- New values push the DAILY curves further apart:
--   - Solar daily now peaks at 6kW at noon (matches the InverterModel's MaxDcInputKw),
--     still 0 at night.
--   - Consumer daily now peaks at 8kW at night (already 180-degrees out of phase from
--     solar, unchanged) and drops to ~1kW at noon.
--   That means a ~8kW deficit for a good stretch of every night, which saturates the
--   battery's 5kW discharge cap for hours at a time -- a single 100kWh battery (usable
--   ~90kWh between minKWH/maxKWH) now drains in under 2 nights instead of 6+ days, and
--   the ~5kW midday surplus recharges it on a similar timescale. Noise (NoiseStdDevKw)
--   is also bumped up on both so the swing isn't a perfectly clean sine wave.
--
-- Only touches the two solar rows and two consumer rows seeded by reset-and-seed.sql
-- (matched by device Discriminator + PeriodHours, not hardcoded IDs). Price simulations
-- are untouched.
--
-- Review before running; run yourself (not something I'll execute).

BEGIN;

-- Solar: daily curve (0 -> 6kW instead of 0 -> 5kW)
UPDATE "Simulations" s
SET "MeanKw" = 3, "AmplitudeKw" = 3, "NoiseStdDevKw" = 0.5
FROM "Devices" d
WHERE s."DeviceId" = d."Id"
  AND d."Discriminator" = 'SolarPowerPlant'
  AND s."PeriodHours" = 24;

-- Solar: weekly drift (slightly wider)
UPDATE "Simulations" s
SET "AmplitudeKw" = 0.7, "NoiseStdDevKw" = 0.15
FROM "Devices" d
WHERE s."DeviceId" = d."Id"
  AND d."Discriminator" = 'SolarPowerPlant'
  AND s."PeriodHours" = 168;

-- Consumer: daily curve (1 -> 8kW instead of 0.4 -> 2kW, same phase so it still peaks at night)
UPDATE "Simulations" s
SET "MeanKw" = 4.5, "AmplitudeKw" = 3.5, "NoiseStdDevKw" = 0.4
FROM "Devices" d
WHERE s."DeviceId" = d."Id"
  AND d."Discriminator" = 'Consumer'
  AND s."PeriodHours" = 24;

-- Consumer: weekly drift (slightly wider)
UPDATE "Simulations" s
SET "AmplitudeKw" = 0.5, "NoiseStdDevKw" = 0.15
FROM "Devices" d
WHERE s."DeviceId" = d."Id"
  AND d."Discriminator" = 'Consumer'
  AND s."PeriodHours" = 168;

COMMIT;
