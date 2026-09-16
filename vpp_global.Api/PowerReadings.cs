using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

public interface IPowerMeterReader
{
    Task<double> ReadCurrentPowerKw(int deviceId);
    Task<double> ReadPowerKwAt(int deviceId, DateTime at); 
}
// TODO: Actual readings in the future
public class SimulatedPowerMeterReader : IPowerMeterReader
{
    private readonly VppDbContext _db;
    public SimulatedPowerMeterReader(VppDbContext db) => _db = db;
    readonly Random _rng = new();
    // Fixed forever, not per-instance: SimulatedPowerMeterReader is created fresh on
    // every request (it's Scoped), so a per-instance "DateTime.UtcNow" epoch would give
    // every tick a different phase reference — breaking correlation between backfilled/
    // analyzed history and live-polled values. A hardcoded constant means the same
    // device always produces the same value at the same absolute timestamp, no matter
    // when or how many times it's asked, even across app restarts.
    static readonly DateTime _epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly Dictionary<int, List<Simulation>> _cache = new();
    private async Task <List<Simulation>> GetSimulationsAsync(int deviceId)
    {
        if(!_cache.TryGetValue(deviceId, out var sims))
        {
            sims = await _db.Set<Simulation>()
            .Where(r => r.DeviceId == deviceId)
            .ToListAsync();
            _cache[deviceId] = sims;
        }
        return sims;
    }

    public Task<double> ReadCurrentPowerKw(int deviceId) => ReadPowerKwAt(deviceId, DateTime.UtcNow);

    public async Task<double> ReadPowerKwAt(int deviceId, DateTime at)
    {
        var device = await GetDeviceAsync(deviceId);
        switch (device)
        {
            case Generator:
                return await SimulateCurveAsync(deviceId, at);
            case Consumer:
                return -await SimulateCurveAsync(deviceId, at);
            case Accumulator:
                // Evaluated at the exact requested `at`, ramping continuously toward
                // whatever target HomeSysLogic last decided — not snapped to it. This is
                // what makes /devices/{id}/live?at=X genuinely continuous: a poll between
                // ticks sees the ramp's true in-progress value, not a stale step.
                var acc = await GetAccumulatorAsync(deviceId);
                return Accumulator.RampedCurrentAt(acc, at);
            default:
                return 0;
        }
    }

    private readonly Dictionary<int, Device?> _deviceCache = new();
    private async Task<Device?> GetDeviceAsync(int deviceId)
    {
        if (!_deviceCache.TryGetValue(deviceId, out var device))
        {
            // During an ingestion tick this reader shares its DbContext with HomeSysLogic,
            // which already loaded every device on this HomeSystem via Include — checking
            // the change tracker first (an in-memory scan, no round trip) avoids re-fetching
            // data that's already sitting in this same context. A standalone caller (e.g.
            // the bare /devices/{id}/live endpoint, its own fresh scope/context) finds
            // nothing tracked yet and falls through to the original query, unchanged.
            device = _db.ChangeTracker.Entries<Device>().Select(e => e.Entity).FirstOrDefault(d => d.Id == deviceId);
            device ??= await _db.Set<Device>().FirstOrDefaultAsync(d => d.Id == deviceId);
            _deviceCache[deviceId] = device;
        }
        return device;
    }

    private readonly Dictionary<int, Accumulator> _accumulatorCache = new();
    private async Task<Accumulator> GetAccumulatorAsync(int deviceId)
    {
        if (!_accumulatorCache.TryGetValue(deviceId, out var acc))
        {
            // Same reasoning as GetDeviceAsync, but also requires Model to already be
            // loaded (HomeSysLogic includes it) — otherwise fall back to the original
            // single query with its Include, so a standalone caller never gets an
            // accumulator with a null Model.
            acc = _db.ChangeTracker.Entries<Accumulator>().Select(e => e.Entity).FirstOrDefault(a => a.Id == deviceId && a.Model is not null);
            acc ??= await _db.Set<Accumulator>().Include(a => a.Model).FirstAsync(a => a.Id == deviceId);
            _accumulatorCache[deviceId] = acc;
        }
        return acc;
    }

    private async Task<double> SimulateCurveAsync(int deviceId, DateTime at)
    {
        var simulationSettings =await GetSimulationsAsync(deviceId);
        
        double sum = 0;
        foreach(Simulation sim in simulationSettings)
        {
            double hoursSinceEpoch = (at - _epoch).TotalHours;
            double signal = sim.MeanKw + sim.AmplitudeKw * Math.Sin(2 * Math.PI * (hoursSinceEpoch + sim.PhaseShift) / sim.PeriodHours);
            double noise = Noise.SampleGaussianNoise()*sim.NoiseStdDevKw;
            sum+=signal + noise;
        }
        return Math.Max(0,sum);
 
    }
}