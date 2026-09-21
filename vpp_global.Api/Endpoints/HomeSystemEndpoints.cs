using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

namespace vpp_global.Api.Endpoints;

public static class HomeSystemEndpoints
{
    public static void MapHomeSystemEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/home-systems/{homeSystemId:int}/devices", async (int homeSystemId, VppDbContext db, CancellationToken ct) =>
        {
            var devices = await db.Devices
                .Where(d => d.HomeSystemId == homeSystemId)
                .Select(d => new { d.Id, d.Name, Type = EF.Property<string>(d, "Discriminator") })
                .ToListAsync(ct);
            return Results.Ok(devices);
        });

        app.MapGet("/home-systems/{homeSystemId:int}", async (int homeSystemId, VppDbContext db, CancellationToken ct) =>
        {
            var hs = await db.HomeSystems
                .Where(h => h.Id == homeSystemId)
                .Select(h => new { h.Id, h.Name, h.GridNodeId, h.LowPrice, h.HighPrice })
                .FirstOrDefaultAsync(ct);
            return hs is null ? Results.NotFound() : Results.Ok(hs);
        });

        app.MapPut("/home-systems/{homeSystemId:int}", async (int homeSystemId, UpdateHomeSystemRequest request, VppDbContext db, CancellationToken ct) =>
        {
            var hs = await db.HomeSystems.FirstOrDefaultAsync(h => h.Id == homeSystemId, ct);
            if (hs is null) return Results.NotFound();
            hs.LowPrice = request.LowPrice;
            hs.HighPrice = request.HighPrice;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        // Forecast net battery power over a time range (+discharge/-charge), from the same
        // HomeSysLogic formula the live /status value uses. 404 until every Generator and
        // Consumer on this HomeSystem has been analyzed — see LoadPredictionSpectraAsync.
        app.MapGet("/home-systems/{homeSystemId:int}/predict-battery", async (int homeSystemId, DateTime from, DateTime to, int? stepMinutes, VppDbContext db, CancellationToken ct) =>
        {
            var predictableDeviceIds = await db.Set<Device>()
                .Where(d => d.HomeSystemId == homeSystemId && (d is Generator || d is Consumer))
                .Select(d => d.Id)
                .ToListAsync(ct);

            var spectra = await HomeSysLogic.LoadPredictionSpectraAsync(db, predictableDeviceIds, ct);
            if (spectra is null)
                return Results.NotFound("Not every Generator/Consumer on this home system has been analyzed yet.");

            var step = TimeSpan.FromMinutes(Math.Max(1, stepMinutes ?? 60));
            var points = new List<object>();
            for (var t = from; t <= to; t = t.Add(step))
                points.Add(new { Timestamp = t, PredictedKw = HomeSysLogic.PredictBatteryKw(spectra, t) });

            return Results.Ok(points);
        });

        // One bundled payload for the /god debug page — every device on this HomeSystem
        // (with its Simulations) plus the grid node's price Simulations, in one request
        // instead of the half-dozen small ones the normal page makes. Projected in memory
        // (after ToListAsync) so `d as Accumulator` can pull out the fields that only
        // apply to Battery/EV, leaving them null for every other device type.
        app.MapGet("/home-systems/{homeSystemId:int}/god", async (int homeSystemId, VppDbContext db, CancellationToken ct) =>
        {
            var homeSystem = await db.HomeSystems
                .Where(h => h.Id == homeSystemId)
                .Select(h => new { h.Id, h.Name, h.GridNodeId, h.LowPrice, h.HighPrice })
                .FirstOrDefaultAsync(ct);
            if (homeSystem is null) return Results.NotFound();

            var devices = await db.Devices
                .Where(d => d.HomeSystemId == homeSystemId)
                .Include(d => d.Simulations)
                .ToListAsync(ct);

            var deviceDtos = devices.Select(d => new
            {
                d.Id,
                d.Name,
                Type = d.GetType().Name,
                CurrentChargeKWH = (d as Accumulator)?.CurrentChargeKWH,
                CapacityKWH = (d as Accumulator)?.CapacityKWH,
                LowKWH = (d as Accumulator)?.LowKWH,
                MaxKWH = (d as Accumulator)?.MaxKWH,
                MinKWH = (d as Accumulator)?.MinKWH,
                Priority = (d as Accumulator)?.Priority,
                Mode = (d as Accumulator)?.Mode.ToString(),
                Simulations = d.Simulations.Select(s => new
                {
                    s.Id, s.MeanKw, s.AmplitudeKw, s.PeriodHours, s.PhaseShift, s.NoiseStdDevKw, s.AllowNegative
                }),
            });

            var priceSimulations = await db.Simulations
                .Where(s => s.GridNodeId == homeSystem.GridNodeId)
                .Select(s => new { s.Id, s.MeanKw, s.AmplitudeKw, s.PeriodHours, s.PhaseShift, s.NoiseStdDevKw, s.AllowNegative })
                .ToListAsync(ct);

            return Results.Ok(new { HomeSystem = homeSystem, Devices = deviceDtos, PriceSimulations = priceSimulations });
        });

        // Deliberately does NOT re-derive generatedKw/acConsumption/InverterCurrent from
        // live meter reads — HomeSysLogic is the single place that decides what's
        // happening, and everything else (this endpoint included) just reflects its
        // output: NetGridKw/Scenario come straight from its last tick via the status
        // tracker; InverterMode/InverterCurrent/Mode/CurrentChargeKWH/AppliedCurrentKw
        // come straight from the DB rows it wrote (Mode and AppliedCurrentKw both derive
        // from/are set by the same tick). No independent recomputation, so the UI can't
        // show numbers that contradict the scenario that produced them.
        app.MapGet("/home-systems/{homeSystemId:int}/status", async (int homeSystemId, VppDbContext db, HomeSysStatusTracker statusTracker, CancellationToken ct) =>
        {
            var homeSystem = await db.HomeSystems
                .Include(hs => hs.Batteries).ThenInclude(b => b.Model)
                .Include(hs => hs.EVs).ThenInclude(e => e.Model)
                .FirstOrDefaultAsync(hs => hs.Id == homeSystemId, ct);
            if (homeSystem is null) return Results.NotFound();

            var inverter = await db.Set<Inverter>().FirstOrDefaultAsync(i => i.HomeSystemId == homeSystemId, ct);
            if (inverter is null) return Results.NotFound("No inverter configured for this home system.");

            var snapshot = statusTracker.Get(homeSystemId);

            var accumulators = homeSystem.Batteries
                .Cast<Accumulator>()
                .Concat(homeSystem.EVs)
                .Select(acc => new
                {
                    DeviceId = acc.Id,
                    acc.Name,
                    Type = acc is Battery ? "Battery" : "EV",
                    Mode = acc.Mode.ToString(),
                    acc.CurrentChargeKWH,
                    acc.CapacityKWH,
                    SocPercent = acc.CapacityKWH > 0 ? acc.CurrentChargeKWH / acc.CapacityKWH * 100 : 0,
                    CurrentKw = acc.AppliedCurrentKw   // the actual ramp-tracked rate, signed (+discharge/-charge)
                })
                .ToList();

            return Results.Ok(new
            {
                // The exact simulated instant this snapshot was computed at — lets the
                // frontend plot these points on the backend's authoritative clock instead
                // of guessing "now" from its own independently-drifting simulated clock.
                Timestamp = snapshot?.At ?? DateTime.UtcNow,
                InverterMode = inverter.InverterMode.ToString(),
                InverterCurrent = inverter.InverterCurrent,   // magnitude of grid buy/sell, as decided by HomeSysLogic
                Scenario = snapshot?.Scenario ?? "Unknown (HomeSysLogic hasn't ticked yet)",
                // Stable key for coloring the price chart by dispatch reason — see DispatchStatus.
                Status = snapshot?.Status.ToString() ?? "Unknown",
                NetGridKw = snapshot?.NetGridKw ?? 0,
                GeneratedKw = snapshot?.GeneratedKw ?? 0,
                AcConsumption = snapshot?.AcConsumption ?? 0,
                // Curtailed counterparts of the two above — clamped to the inverter's
                // MaxDCInput/MaxOutputPower, still in their native DC/AC domains, for
                // plotting directly against the raw per-device curves on the main chart.
                DeliverableGeneratedKw = snapshot?.DeliverableGeneratedKw ?? 0,
                DeliverableAcConsumption = snapshot?.DeliverableAcConsumption ?? 0,
                // DeliverableGeneratedKw converted to AC — a third, separate quantity.
                ActualGenerationKw = snapshot?.ActualGenerationKw ?? 0,
                // Predicted net battery power (positive=predicted discharge, negative=
                // predicted charge, matching AppliedCurrentKw's own convention) — null
                // until every Generator/Consumer here has been analyzed at least once.
                PredictedBatteryKw = snapshot?.PredictedBatteryKw,
                // True when the raw measured value above exceeds what the inverter can
                // actually pass through — HomeSysLogic already clamps its own dispatch
                // math for this, these just let the UI warn that it's happening.
                Overloaded = snapshot?.Overloaded ?? false,
                Overgenerating = snapshot?.Overgenerating ?? false,
                ActiveAccPriority = homeSystem.ActiveAccPriority,
                Accumulators = accumulators
            });
        });
    }
}

public record UpdateHomeSystemRequest(double LowPrice, double HighPrice);
