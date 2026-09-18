using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

namespace vpp_global.Api.Endpoints;

public static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/simulation/speed", (SimulationClock clock) =>
            Results.Ok(new { clock.HoursPerTick }));

        app.MapPost("/simulation/speed", (SetSimulationSpeedRequest request, SimulationClock clock) =>
        {
            clock.HoursPerTick = request.HoursPerTick;   // setter rebases the clock internally
            return Results.Ok(new { clock.HoursPerTick });
        });

        app.MapGet("/simulation/recording", (RecordingSettings recording) =>
            Results.Ok(new { recording.RecordPowerReadings }));

        app.MapPost("/simulation/recording", (SetRecordingRequest request, RecordingSettings recording) =>
        {
            recording.RecordPowerReadings = request.RecordPowerReadings;
            return Results.Ok(new { recording.RecordPowerReadings });
        });

        // One row = one sine/noise curve component, whether it belongs to a device
        // (solar/consumer/...) or a grid node (price) — same table, same shape, so one
        // endpoint edits both. Picked up on the very next tick: SimulatedPowerMeterReader/
        // SimulatedGrid cache Simulation rows per-request-scope, never across ticks.
        app.MapPut("/simulations/{simulationId:int}", async (int simulationId, UpdateSimulationRequest request, VppDbContext db, CancellationToken ct) =>
        {
            var sim = await db.Simulations.FirstOrDefaultAsync(s => s.Id == simulationId, ct);
            if (sim is null) return Results.NotFound();
            sim.MeanKw = request.MeanKw;
            sim.AmplitudeKw = request.AmplitudeKw;
            sim.PeriodHours = request.PeriodHours;
            sim.PhaseShift = request.PhaseShift;
            sim.NoiseStdDevKw = request.NoiseStdDevKw;
            sim.AllowNegative = request.AllowNegative;
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });
    }
}

public record SetSimulationSpeedRequest(double HoursPerTick);
public record SetRecordingRequest(bool RecordPowerReadings);
public record UpdateSimulationRequest(double MeanKw, double AmplitudeKw, double PeriodHours, double PhaseShift, double NoiseStdDevKw, bool AllowNegative);
