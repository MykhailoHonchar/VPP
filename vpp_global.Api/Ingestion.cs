using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

public class PowerReadingIngestionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HomeSysStatusTracker _status;
    private readonly SimulationClock _clock;
    public PowerReadingIngestionService(IServiceScopeFactory scopeFactory, HomeSysStatusTracker status, SimulationClock clock)
    {
        _scopeFactory = scopeFactory;
        _status = status;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One-time reset on every app start: clears out whatever charge/discharge state
        // was left mid-flight from before the restart (e.g. a target that was never
        // zeroed out because the app stopped before the next tick could apply it), and
        // starts every battery/EV fully charged rather than at whatever SoC it happened
        // to be at when the app last stopped.
        using (var startupScope = _scopeFactory.CreateScope())
        {
            var startupDb = startupScope.ServiceProvider.GetRequiredService<VppDbContext>();
            var accumulators = await startupDb.Set<Accumulator>().ToListAsync(stoppingToken);
            foreach (var acc in accumulators)
            {
                acc.TargetCurrentKw = 0;
                acc.AppliedCurrentKw = 0;
                acc.CurrentChargeKWH = acc.CapacityKWH;
                acc.LastTickAt = null;
                acc.Mode = AccumulatorMode.Full;   // matches CurrentChargeKWH = CapacityKWH, rather than waiting for the first tick's ComputeMode to catch up
            }
            await startupDb.SaveChangesAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        DateTime? lastTickSimTime = null;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = _clock.Now();
            // Actual elapsed simulated time since the last tick — not _clock.HoursPerTick
            // (that's the configured rate, hours per real *second*). Those two only
            // happened to be numerically equal back when the tick period was fixed at
            // exactly 1 real second; deriving it from real elapsed time instead keeps SoC
            // integration correct regardless of the tick interval or any timing jitter.
            var hoursPerTick = lastTickSimTime is null ? 0 : (now - lastTickSimTime.Value).TotalHours;
            lastTickSimTime = now;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VppDbContext>();
            var meter = scope.ServiceProvider.GetRequiredService<IPowerMeterReader>();
            var pricer = scope.ServiceProvider.GetRequiredService<IGridPriceProvider>();

            // Flattened from a Region -> GridNode -> HomeSystem nested loop (1 + R + N
            // round trips just to discover which home systems exist) to a single query —
            // nothing in the loop body actually needs the region/grid-node grouping, only
            // final HomeSystem membership. Each round trip to Supabase here costs tens of
            // ms, so cutting redundant ones directly shortens the real tick period.
            var homeSystemIds = await db.Set<HomeSystem>().Select(hs => hs.Id).ToListAsync(stoppingToken);
            foreach (var hsId in homeSystemIds)
            {
                var logic = await HomeSysLogic.CreateAsync(hsId, db, meter, pricer, stoppingToken);
                var result = await logic.ReadHomeSysPowerAsync(now, hoursPerTick);
                _status.Set(hsId, new HomeSysSnapshot(result.Scenario, result.Status, result.NetGridKw, result.GeneratedKw, result.AcConsumption, result.DeliverableGeneratedKw, result.DeliverableAcConsumption, result.ActualGenerationKw, result.Overloaded, result.Overgenerating, now));
            }
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
