using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

namespace vpp_global.Api.Endpoints;

public static class GridEndpoints
{
    public static void MapGridEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/grid/{gridNodeId:int}/net-power", async (int gridNodeId, DateTime? at, IGridPriceProvider gridPrice) =>
        {
            var timestamp = at ?? DateTime.UtcNow;
            var price = await gridPrice.GetPriceAt(gridNodeId, timestamp);
            return Results.Ok(new { Timestamp = timestamp, Price = price });
        });

        app.MapGet("/grid/{gridNodeId:int}/live", async (int gridNodeId, DateTime? at, IGridPriceProvider gridPrice, SimulationClock clock) =>
        {
            // Default to the backend's own authoritative simulated clock — see the same
            // reasoning on /devices/{id}/live. Explicit `at` (used by GridNodePage's own
            // manually-stepped clock) is still honored unchanged.
            var timestamp = at ?? clock.Now();
            var kw = await gridPrice.GetPriceAt(gridNodeId, timestamp);
            return Results.Ok(new { Timestamp = timestamp, PowerKw = kw });
        });

        app.MapPost("/grid/{gridNodeId:int}/readings/generate", async (int gridNodeId, GenerateReadingsRequest request, VppDbContext db, IGridPriceProvider gridPrice, CancellationToken ct) =>
        {
            var now = DateTime.UtcNow;
            var start = now.AddDays(-request.Days);

            for (var hour = start; hour <= now; hour = hour.AddHours(1))
            {
                var kw = await gridPrice.GetPriceAt(gridNodeId, hour);
                db.PowerReadings.Add(new PowerReading { GridNodeId = gridNodeId, Timestamp = hour, PowerKw = kw });
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        app.MapPost("/grid/{gridNodeId:int}/analyze", async (int gridNodeId, IPowerSpectrumAnalyzer analyzer, CancellationToken ct) =>
        {
            await analyzer.AnalyzeGridNode(gridNodeId, ct);
            return Results.Ok();
        });

        app.MapGet("/grid/{gridNodeId:int}/readings", async (int gridNodeId, int? days, VppDbContext db, CancellationToken ct) =>
        {
            var since = DateTime.UtcNow.AddDays(-(days ?? 30));
            var readings = await db.PowerReadings
                .Where(r => r.GridNodeId == gridNodeId && r.Timestamp >= since)
                .OrderBy(r => r.Timestamp)
                .Select(r => new { r.Timestamp, r.PowerKw })
                .ToListAsync(ct);
            return Results.Ok(readings);
        });

        app.MapGet("/grid/{gridNodeId:int}/predict", async (int gridNodeId, int? days, DateTime? from, DateTime? to, VppDbContext db, CancellationToken ct) =>
        {
            var spectrum = await db.PowerSpectra
                .Include(s => s.Components)
                .FirstOrDefaultAsync(s => s.GridNodeId == gridNodeId, ct);

            if (spectrum is null)
                return Results.NotFound("No spectrum computed yet for this grid node — run /analyze first.");

            var since = from ?? DateTime.UtcNow.AddDays(-(days ?? 30));
            var until = to ?? DateTime.UtcNow;

            var points = new List<object>();
            for (var t = since; t <= until; t = t.AddHours(1))
                points.Add(new { Timestamp = t, PredictedKw = PowerPredictor.Predict(spectrum, t) });

            return Results.Ok(points);
        });
    }
}
