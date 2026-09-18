using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

public class PowerReadingIngestionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HomeSysStatusTracker _status;
    private readonly SimulationClock _clock;
    private readonly RecordingSettings _recording;
    public PowerReadingIngestionService(IServiceScopeFactory scopeFactory, HomeSysStatusTracker status, SimulationClock clock, RecordingSettings recording)
    {
        _scopeFactory = scopeFactory;
        _status = status;
        _clock = clock;
        _recording = recording;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                acc.Mode = AccumulatorMode.Full;  
            }
            await startupDb.SaveChangesAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        DateTime? lastTickSimTime = null;
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = _clock.Now();
            var hoursPerTick = lastTickSimTime is null ? 0 : (now - lastTickSimTime.Value).TotalHours;
            lastTickSimTime = now;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VppDbContext>();
            var meter = scope.ServiceProvider.GetRequiredService<IPowerMeterReader>();
            var pricer = scope.ServiceProvider.GetRequiredService<IGridPriceProvider>();
            var homeSystemIds = await db.Set<HomeSystem>().Select(hs => hs.Id).ToListAsync(stoppingToken);
            foreach (var hsId in homeSystemIds)
            {
                var logic = await HomeSysLogic.CreateAsync(hsId, db, meter, pricer, _recording, stoppingToken);
                var result = await logic.ReadHomeSysPowerAsync(now, hoursPerTick, stoppingToken);
                _status.Set(hsId, new HomeSysSnapshot(result.Scenario, result.Status, result.NetGridKw, result.GeneratedKw, result.AcConsumption, result.DeliverableGeneratedKw, result.DeliverableAcConsumption, result.ActualGenerationKw, result.PredictedBatteryKw, result.Overloaded, result.Overgenerating, now));
            }
            await db.SaveChangesAsync(stoppingToken);
        }
    }
}
