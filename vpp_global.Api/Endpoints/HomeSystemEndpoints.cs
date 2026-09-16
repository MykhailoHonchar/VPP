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
                .Select(h => new { h.Id, h.Name, h.GridNodeId, h.lowPrice, h.highPrice })
                .FirstOrDefaultAsync(ct);
            return hs is null ? Results.NotFound() : Results.Ok(hs);
        });

        // Deliberately does NOT re-derive generatedKw/acConsumption/InverterCurrent from
        // live meter reads — HomeSysLogic is the single place that decides what's
        // happening, and everything else (this endpoint included) just reflects its
        // output: NetGridKw/Scenario come straight from its last tick via the status
        // tracker; InverterMode/InverterCurrent/Mode/CurrentChargeKWH/appliedCurrentKw
        // come straight from the DB rows it wrote (Mode and appliedCurrentKw both derive
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
                    CurrentKw = acc.appliedCurrentKw   // the actual ramp-tracked rate, signed (+discharge/-charge)
                })
                .ToList();

            return Results.Ok(new
            {
                InverterMode = inverter.InverterMode.ToString(),
                InverterCurrent = inverter.InverterCurrent,   // magnitude of grid buy/sell, as decided by HomeSysLogic
                Scenario = snapshot?.Scenario ?? "Unknown (HomeSysLogic hasn't ticked yet)",
                NetGridKw = snapshot?.NetGridKw ?? 0,
                GeneratedKw = snapshot?.GeneratedKw ?? 0,
                AcConsumption = snapshot?.AcConsumption ?? 0,
                GeneratedAcKw = (snapshot?.GeneratedKw ?? 0) * inverter.dc2acEfficiency,
                ActiveAccPriority = homeSystem.ActiveAccPriority,
                Accumulators = accumulators
            });
        });
    }
}
