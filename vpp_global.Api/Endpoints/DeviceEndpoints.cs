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

            var now = DateTime.UtcNow;
            var start = now.AddDays(-request.Days);

            for (var hour = start; hour <= now; hour = hour.AddHours(1))
            {
                var kw = await meter.ReadPowerKwAt(deviceId, hour);
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

        app.MapGet("/devices/{deviceId:int}/live", async (int deviceId, DateTime? at, IPowerMeterReader meter) =>
        {
            var timestamp = at ?? DateTime.UtcNow;
            var kw = await meter.ReadPowerKwAt(deviceId, timestamp);
            return Results.Ok(new { Timestamp = timestamp, PowerKw = kw });
        });

        // Not functional: Mode is now derived from appliedCurrentKw's sign (see
        // Accumulator.Mode), and HomeSysLogic overwrites targetCurrentKw every tick
        // regardless. A real manual override needs HomeSysLogic itself to respect an
        // override flag — deferred until the interactive override UI is built.
        app.MapPost("/devices/{deviceId:int}/battery-mode", (int deviceId, AccumulatorMode mode) =>
            Results.Problem("Manual battery mode override isn't implemented yet.", statusCode: 501));
    }
}

public record GenerateReadingsRequest(int Days);
