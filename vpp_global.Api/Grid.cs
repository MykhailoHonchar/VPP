using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

public interface IGridPriceProvider
{
    Task<double> GetPriceKw(int deviceId);
    Task<double> GetPriceAt(int deviceId, DateTime at); 
}
// TODO: Actual readings in the future
public class SimulatedGrid : IGridPriceProvider
{
    private readonly VppDbContext _db;
    public SimulatedGrid(VppDbContext db) => _db = db;
    static readonly DateTime _epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);


    private readonly Dictionary<int, List<Simulation>> _cache = new();
    private async Task <List<Simulation>> GetSimulationsAsync(int gridNodeId)
    {
        if(!_cache.TryGetValue(gridNodeId, out var sims))
        {
            sims = await _db.Set<Simulation>()
            .Where(r => r.GridNodeId == gridNodeId)
            .ToListAsync();
            _cache[gridNodeId] = sims;
        }
        return sims;
    }

    public Task<double> GetPriceKw(int gridNodeId) => GetPriceAt(gridNodeId, DateTime.UtcNow);

    public async Task<double> GetPriceAt(int gridNodeId, DateTime at)
    {
        
        var simulationSettings =await GetSimulationsAsync(gridNodeId);
        
        double sum = 0;
        foreach(Simulation sim in simulationSettings)
        {
            double hoursSinceEpoch = (at - _epoch).TotalHours;
            double signal = sim.MeanKw + sim.AmplitudeKw * Math.Sin(2 * Math.PI * (hoursSinceEpoch + sim.PhaseShift) / sim.PeriodHours);
            
            double noise = Noise.SampleGaussianNoise() * sim.NoiseStdDevKw;
            sum+=signal + noise;
            if(!sim.AllowNegative && sum<0) sum = 0;
        }
        sum=Math.Max(0,sum);
        return sum;

    }
}