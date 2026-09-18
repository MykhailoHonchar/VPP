using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

namespace vpp_global.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/devices/{deviceId:int}/readings/generate", async (int deviceId, GenerateReadingsRequest request, VppDbContext db, IPowerMeterReader meter, CancellationToken ct) =>
        {
            var device = await db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is null) return Results.NotFound();
            if (device is Accumulator)
                return Results.BadRequest("Backfill is not supported for Accumulator devices — SOC is stateful and has no meaningful historical value outside of actually-simulated ticks.");

            // Curtailed the same way /devices/{id}/live is — see DeviceCurtailment —
            // so a backfilled, analyzed, and predicted curve describes the same clamped
            // signal a live chart read would.
            var inverter = await db.Set<Inverter>().FirstOrDefaultAsync(i => i.HomeSystemId == device.HomeSystemId, ct);

            var now = DateTime.UtcNow;
            var start = now.AddDays(-request.Days);

            for (var hour = start; hour <= now; hour = hour.AddHours(1))
            {
                var kw = await meter.ReadPowerKwAt(deviceId, hour);
                if (inverter is not null) kw = DeviceCurtailment.Apply(device, inverter, kw);
                db.PowerReadings.Add(new PowerReading { DeviceId = deviceId, Timestamp = hour, PowerKw = kw });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        app.MapPost("/devices/{deviceId:int}/analyze", async (int deviceId, IPowerSpectrumAnalyzer analyzer, CancellationToken ct) =>
        {
            await analyzer.Analyze(deviceId, ct);



            return Results.Ok();
        });

        app.MapGet("/devices/{deviceId:int}/readings", async (int deviceId, int? days, VppDbContext db, CancellationToken ct) =>
        {
            var since = DateTime.UtcNow.AddDays(-(days ?? 30));
            var readings = await db.PowerReadings
                .Where(r => r.DeviceId == deviceId && r.Timestamp >= since)
                .OrderBy(r => r.Timestamp)
                .Select(r => new { r.Timestamp, r.PowerKw })
                .ToListAsync(ct);
            return Results.Ok(readings);
        });

        app.MapGet("/devices/{deviceId:int}/spectral_components", async (int deviceId, VppDbContext db, CancellationToken ct) =>
        {
            var components = await db.SpectralComponents
                .Where(sc => sc.PowerSpectrum.DeviceId == deviceId)
                .OrderByDescending(sc => sc.SignificanceScore)
                .Select(sc => new { sc.PeriodHours, sc.AmplitudeKw, sc.PhaseRadians, sc.SignificanceScore })
                .ToListAsync(ct);

            return Results.Ok(components);
        });

        app.MapGet("/devices/{deviceId:int}/spectrum_debug", async (int deviceId, IPowerSpectrumAnalyzer analyzer, CancellationToken ct) =>
        {
            var spectrum = await analyzer.GetRawSpectrum(deviceId, ct);
            return Results.Ok(spectrum);
        });

        app.MapGet("/devices/{deviceId:int}/predict", async (int deviceId, int? days, DateTime? from, DateTime? to, VppDbContext db, CancellationToken ct) =>
        {
            var spectrum = await db.PowerSpectra
                .Include(s => s.Components)
                .Include(s => s.Device)
                .FirstOrDefaultAsync(s => s.DeviceId == deviceId, ct);

            if (spectrum is null)
                return Results.NotFound("No spectrum computed yet for this device — run /analyze first.");

    
            var since = from ?? DateTime.UtcNow.AddDays(-(days ?? 30));
            var until = to ?? DateTime.UtcNow;

            var points = new List<object>();
            for (var t = since; t <= until; t = t.AddHours(1))
                points.Add(new { Timestamp = t, PredictedKw = PowerPredictor.Predict(spectrum, t) });

            return Results.Ok(points);
        });

        app.MapGet("/devices/{deviceId:int}/live", async (int deviceId, DateTime? at, IPowerMeterReader meter, SimulationClock clock, VppDbContext db, CancellationToken ct) =>
        {
            // Default to the backend's own authoritative simulated clock, not the
            // caller's guess — a client-supplied `at` for an Accumulator can fall behind
            // the ramp's own LastTickAt (advanced independently by the ingestion
            // service's internal tick), which clamps RampedCurrentAt's elapsed time to
            // zero and returns a frozen value instead of a genuinely live one. Explicit
            // `at` is still honored for callers that need a specific past/future instant.
            var timestamp = at ?? clock.Now();
            var kw = await meter.ReadPowerKwAt(deviceId, timestamp);

            // Curtail via the same DeviceCurtailment logic used for backfilled/recorded
            // PowerReadings, so the main chart's device line, the analyzed history, and a
            // live read never disagree about what's actually deliverable. HomeSysLogic's
            // own internal aggregation stays uncurtailed regardless — that's the true
            // measurement "Pure Gen"/"Consumption" on the Live State panel report.
            var device = await db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId, ct);
            if (device is not null)
            {
                var inverter = await db.Set<Inverter>()
                    .FirstOrDefaultAsync(i => i.HomeSystemId == device.HomeSystemId, ct);
                if (inverter is not null) kw = DeviceCurtailment.Apply(device, inverter, kw);
            }

            return Results.Ok(new { Timestamp = timestamp, PowerKw = kw });
        });

        // Not functional: Mode is now derived from AppliedCurrentKw's sign (see
        // Accumulator.Mode), and HomeSysLogic overwrites TargetCurrentKw every tick
        // regardless. A real manual override needs HomeSysLogic itself to respect an
        // override flag — deferred until the interactive override UI is built.
        app.MapPost("/devices/{deviceId:int}/battery-mode", (int deviceId, AccumulatorMode mode) =>
            Results.Problem("Manual battery mode override isn't implemented yet.", statusCode: 501));

        // For the /god debug page — instance-level Accumulator (Battery/EV) fields only.
        // Deliberately does NOT touch AccumulatorModel (MaxChargeKw/RampRateKwPerHour/...):
        // several batteries share the same Model row in the seed data, so editing it here
        // would silently change every battery using that model, not just this one.
        app.MapPut("/devices/{deviceId:int}/accumulator", async (int deviceId, UpdateAccumulatorRequest request, VppDbContext db, CancellationToken ct) =>
        {
            var acc = await db.Set<Accumulator>().FirstOrDefaultAsync(a => a.Id == deviceId, ct);
            if (acc is null) return Results.NotFound("Not a Battery/EV device.");
            acc.CurrentChargeKWH = request.CurrentChargeKWH;
            acc.CapacityKWH = request.CapacityKWH;
            acc.LowKWH = request.LowKWH;
            acc.MaxKWH = request.MaxKWH;
            acc.MinKWH = request.MinKWH;
            acc.Priority = request.Priority;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }
}

public record GenerateReadingsRequest(int Days);
public record UpdateAccumulatorRequest(double CurrentChargeKWH, double CapacityKWH, double LowKWH, double MaxKWH, double MinKWH, int Priority);
