-- Adds an Inverter (+ its InverterModel) for HomeSystemId = 1.
-- Change the "1" below if this is actually for a different home system.
-- Review before running; run yourself.

BEGIN;

WITH
new_inverter_model AS (
    INSERT INTO "InverterModels" ("Name", "Manufacturer", "RatedPowerKw", "MaxDcInputKw", "EfficiencyPercent", "IsHybrid")
    VALUES ('Standard Hybrid Inverter', 'InverTech', 5.0, 6.0, 97, true)
    RETURNING "Id"
),
new_inverter AS (
    INSERT INTO "Devices"
        ("Discriminator", "HomeSystemId", "Name", "Inverter_ModelId",
         "InverterCurrent", "InverterMode", "MaxDCInput", "MaxOutputPower",
         "ac2dcEfficiency", "dc2acEfficiency", "onAcc", "onGrid")
    SELECT 'Inverter', 1, 'Inverter 1', "Id",
           0, 0, 6.0, 5.0,
           0.97, 0.97, true, true
    FROM new_inverter_model
    RETURNING "Id"
)
SELECT "Id" AS inverter_device_id FROM new_inverter;

COMMIT;
