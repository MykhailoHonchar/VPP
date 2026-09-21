using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

public enum DispatchStatus { SellBatteryFull, SellHighPrice, SellNoBattery, BuyBatteryEmpty, BuyLowPrice, BuyNoBattery, ChargeOffgrid, DrainOffgrid }

public class HomeSysLogic
{
    public record HomeSysPowerRes(string Scenario, DispatchStatus Status, double NetGridKw, double GeneratedKw, double AcConsumption, double DeliverableGeneratedKw, double DeliverableAcConsumption, double ActualGenerationKw, double? PredictedBatteryKw, bool Overloaded, bool Overgenerating);
    private readonly VppDbContext db;
    private readonly IPowerMeterReader meter;
    private readonly IGridPriceProvider pricer;
    private readonly RecordingSettings recording;
    private readonly HomeSystem homeSystem;
    private readonly Inverter inverter;

    public static async Task<HomeSysLogic> CreateAsync(int homeSystemId, VppDbContext _db, IPowerMeterReader _meter, IGridPriceProvider _pricer, RecordingSettings _recording, CancellationToken ct = default)
    {
        var homeSystem = await _db.HomeSystems
            .Include(hs => hs.Generators)
            .Include(hs => hs.Batteries).ThenInclude(b => b.Model)
            .Include(hs => hs.EVs).ThenInclude(e => e.Model)
            .Include(hs => hs.Consumers)
            .Include(hs => hs.GridNode)
            .FirstAsync(hs => hs.Id == homeSystemId, ct);

        var inverter = await _db.Set<Inverter>()
            .Include(i => i.InverterModel)
            .FirstAsync(i => i.HomeSystemId == homeSystemId, ct);

        return new HomeSysLogic(_db, _meter, _pricer, _recording, homeSystem, inverter);
    }

    private HomeSysLogic(VppDbContext _db, IPowerMeterReader _meter, IGridPriceProvider _pricer, RecordingSettings _recording, HomeSystem _homeSystem, Inverter _inverter)
    {
        db = _db;
        meter = _meter;
        pricer = _pricer;
        recording = _recording;
        homeSystem = _homeSystem;
        inverter = _inverter;
    }

    public static async Task<List<PowerSpectrum>?> LoadPredictionSpectraAsync(VppDbContext db, IReadOnlyCollection<int> predictableDeviceIds, CancellationToken ct = default)
    {
        if (predictableDeviceIds.Count == 0) return null;
        var spectra = await db.Set<PowerSpectrum>()
            .Include(s => s.Components)
            .Include(s => s.Device)
            .Where(s => s.DeviceId != null && predictableDeviceIds.Contains(s.DeviceId.Value))
            .ToListAsync(ct);
        return spectra.Count == predictableDeviceIds.Count ? spectra : null;
    }
    public static double PredictBatteryKw(IEnumerable<PowerSpectrum> spectra, DateTime at) =>
        -spectra.Sum(s => PowerPredictor.Predict(s, at));

    // One future instant. GeneratorsKw is positive, ConsumersKw negative (the same sign as
    // their live readings), BatteryKw is +discharge/-charge — see PredictBatteryKw.
    public record ForecastPoint(DateTime At, double GeneratorsKw, double ConsumersKw, double BatteryKw);

    const double ForecastHorizonHours = 24;
    const double ForecastStepHours = 1;

    // Points at `from` + step, + 2*step, ... up to `from` + horizon (strictly in the future;
    // the value for `from` itself is PredictedBatteryKw). Generators/consumers are summed
    // across every device of that kind; batteries are one aggregate, not per battery.
    public static List<ForecastPoint> BuildForecast(IReadOnlyCollection<PowerSpectrum> spectra, IReadOnlySet<int> generatorIds, DateTime from, TimeSpan horizon, TimeSpan step)
    {
        var points = new List<ForecastPoint>();
        for (var t = from + step; t <= from + horizon; t += step)
        {
            double generators = spectra.Where(s => s.DeviceId is int id && generatorIds.Contains(id)).Sum(s => PowerPredictor.Predict(s, t));
            double consumers = spectra.Where(s => !(s.DeviceId is int id && generatorIds.Contains(id))).Sum(s => PowerPredictor.Predict(s, t));
            points.Add(new ForecastPoint(t, generators, consumers, PredictBatteryKw(spectra, t)));
        }
        return points;
    }

    public async Task<HomeSysPowerRes> ReadHomeSysPowerAsync(DateTime at, double hoursPerTick, CancellationToken ct = default)
    {
        double generatedKw = 0;
        double acConsumption = 0;

        async Task ReadDeviceAsync(Device device, bool isDcContributor, bool isAcConsumer)
        {
            var val = await meter.ReadPowerKwAt(device.Id, at);
            if (recording.RecordPowerReadings)
                db.Add(new PowerReading { DeviceId = device.Id, Timestamp = at, PowerKw = DeviceCurtailment.Apply(device, inverter, val) });
            if (isDcContributor) generatedKw += val;
            if(isAcConsumer) acConsumption += -val;   // val is negative for Consumer; store demand as a positive kW magnitude
        }

        foreach (var g in homeSystem.Generators) await ReadDeviceAsync(g, isDcContributor: true, isAcConsumer: false);
        foreach (var c in homeSystem.Consumers) await ReadDeviceAsync(c, isDcContributor: false, isAcConsumer: true);

        double? predictedBatteryKw = null;
        var predictableDeviceIds = homeSystem.Generators.Select(g => g.Id)
            .Concat(homeSystem.Consumers.Select(c => c.Id))
            .ToList();
        var spectra = await LoadPredictionSpectraAsync(db, predictableDeviceIds, ct);
        if (spectra is not null)
            predictedBatteryKw = PredictBatteryKw(spectra, at);

        // Predicted generators/consumers/batteries over the next ForecastHorizonHours, one
        // point per ForecastStepHours. Empty (not null) until every Generator and Consumer
        // has been analyzed, so a `foreach` over it just does nothing in that case.
        List<ForecastPoint> forecast = spectra is null
            ? []
            : BuildForecast(spectra, homeSystem.Generators.Select(g => g.Id).ToHashSet(), at, TimeSpan.FromHours(ForecastHorizonHours), TimeSpan.FromHours(ForecastStepHours));

        bool overloaded = acConsumption > inverter.MaxOutputPower;
        bool overgenerating = generatedKw > inverter.MaxDCInput;
        double deliverableAcConsumption = overloaded ? inverter.MaxOutputPower : acConsumption;
        double deliverableGeneratedKw = overgenerating ? inverter.MaxDCInput : generatedKw;

        var powerPrice = await pricer.GetPriceAt(homeSystem.GridNodeId, at);
        var allAccumulators = homeSystem.Batteries.Cast<Accumulator>().Concat(homeSystem.EVs).ToList();
        int maxPriority = allAccumulators.Count > 0 ? allAccumulators.Max(a => a.Priority) : 0;

        Accumulator? aa = allAccumulators.SingleOrDefault(a => a.Priority == homeSystem.ActiveAccPriority);
        Accumulator? firstAcc = allAccumulators.SingleOrDefault(a => a.Priority == 0);
        Accumulator? lastAcc = allAccumulators.SingleOrDefault(a => a.Priority == maxPriority);

        ///switch to next batt?
        Accumulator changeCell(bool next, Accumulator acc)
        {
            Accumulator? nextAA = allAccumulators.SingleOrDefault(a => a.Priority == homeSystem.ActiveAccPriority + (next ? 1 : -1));

            if (nextAA is not null)
            {
                acc.TargetCurrentKw = 0;  
                homeSystem.ActiveAccPriority += next ? 1 : -1;
                return nextAA;
            }
            return acc;
        }
        if(aa is not null && aa.Mode == AccumulatorMode.Empty && homeSystem.ActiveAccPriority!=maxPriority)
        {
            //drain next
            aa = changeCell(true, aa);
            aa.Mode = AccumulatorMode.Draining;
        }
        if(aa is not null && aa.Mode == AccumulatorMode.Full && homeSystem.ActiveAccPriority!=0)
        {
            //charge next
            aa = changeCell(false, aa);
            aa.Mode = AccumulatorMode.Charging;
        }

        string scenario;
        DispatchStatus status;
        double netGridKw = deliverableGeneratedKw*inverter.dc2acEfficiency-deliverableAcConsumption;
        //if we produce enough to cover our demand


        if(netGridKw>0)
        {
            //is there batt?
            if(aa is null || firstAcc is null)
            {
                ///sell
                inverter.InverterMode = InverterMode.Selling;
                status = DispatchStatus.SellNoBattery;
                scenario = "Producing enough, no battery — Sell";
            }



            //is batt ok?
            else if(firstAcc.CurrentChargeKWH > firstAcc.LowKWH && (firstAcc.CurrentChargeKWH >= firstAcc.MaxKWH || powerPrice > homeSystem.HighPrice))
            {
                inverter.InverterMode = InverterMode.Selling;
                firstAcc.TargetCurrentKw = 0;
                if (firstAcc.CurrentChargeKWH >= firstAcc.MaxKWH)
                {
                    //sell full
                    status = DispatchStatus.SellBatteryFull;
                    scenario = "Producing enough, battery full — Sell";
                }
                else
                {
                    //drain sell
                    status = DispatchStatus.SellHighPrice;
                    scenario = "Producing enough, price high — Sell";
                }
            }
            //batt low
            else
            {
                ///charge offgrid
                inverter.InverterMode = InverterMode.Offgrid;
                status = DispatchStatus.ChargeOffgrid;
                scenario = "Producing enough, battery low — Charge (offgrid)";
            }
        }
        else
        {
            //is there batt?
            if(aa is null || lastAcc is null)
            {
                //buy
                inverter.InverterMode = InverterMode.Buying;
                status = DispatchStatus.BuyNoBattery;
                scenario = "Not enough production, no battery — Buy";
            }
            //is batt low?
            else if(lastAcc.CurrentChargeKWH < lastAcc.LowKWH &&(lastAcc.CurrentChargeKWH <= lastAcc.MinKWH || powerPrice < homeSystem.LowPrice))
            {
                ///buy idle
                inverter.InverterMode = InverterMode.Buying;
                
               
                if (lastAcc.CurrentChargeKWH <= lastAcc.MinKWH)
                {
                     //force buy
                    status = DispatchStatus.BuyBatteryEmpty;
                    scenario = "Not enough production, battery empty — Buy";
                }
                else
                {
                    //buy cheap
                    status = DispatchStatus.BuyLowPrice;
                    scenario = "Not enough production, price low — Buy";
                }
            }
            //batt ok
            else
            {
                ///Drain offgrid
                inverter.InverterMode = InverterMode.Offgrid;
                status = DispatchStatus.DrainOffgrid;
                scenario = "Not enough production, battery ok — Drain (offgrid)";
            }
        }
        double extraACPower = inverter.MaxOutputPower-deliverableAcConsumption;

        switch(status)
        {
            case DispatchStatus.SellBatteryFull:
            case DispatchStatus.SellNoBattery:
                inverter.InverterCurrent = Math.Min(netGridKw, extraACPower);
                break;
            case DispatchStatus.SellHighPrice:
                double extraDischargeKw = aa!.CurrentChargeKWH > aa.LowKWH
                    ? Math.Clamp(extraACPower - deliverableGeneratedKw*inverter.dc2acEfficiency, 0, aa.Model.MaxDischargeKw)
                    : 0;
                aa.TargetCurrentKw = extraDischargeKw;
                inverter.InverterCurrent = Math.Min(netGridKw + extraDischargeKw, extraACPower);
                break;
            case DispatchStatus.BuyBatteryEmpty:
                aa!.TargetCurrentKw = 0;
                inverter.InverterCurrent = netGridKw;
                break;
            case DispatchStatus.BuyLowPrice:
                aa!.TargetCurrentKw = -Math.Min(aa.Model.MaxChargeKw, extraACPower*inverter.ac2dcEfficiency);
                inverter.InverterCurrent = -Math.Min(extraACPower, -aa.TargetCurrentKw/inverter.ac2dcEfficiency);
                break;
            case DispatchStatus.BuyNoBattery:
                inverter.InverterCurrent = netGridKw;
                break;
            case DispatchStatus.ChargeOffgrid:
                inverter.InverterCurrent = 0;
                aa!.TargetCurrentKw = -Math.Min(aa.Model.MaxChargeKw, netGridKw);
                break;
            case DispatchStatus.DrainOffgrid:
                inverter.InverterCurrent = 0;
                aa!.TargetCurrentKw = Math.Min(aa.Model.MaxDischargeKw, -netGridKw/inverter.dc2acEfficiency);
                break;
        }


        foreach (var acc in allAccumulators)
        {
            double oldApplied = acc.AppliedCurrentKw;
            double newApplied = Accumulator.RampedCurrentAt(acc, at);
            acc.AppliedCurrentKw = newApplied;

            double avgAppliedKw = (oldApplied + newApplied) / 2;   // trapezoidal — exact for a linear ramp
            acc.CurrentChargeKWH = Math.Clamp(acc.CurrentChargeKWH - avgAppliedKw * hoursPerTick, acc.MinKWH, acc.MaxKWH);
            acc.LastTickAt = at;
            acc.Mode = Accumulator.ComputeMode(acc);

            await ReadDeviceAsync(acc, isDcContributor: false, isAcConsumer: false);
        }
        return new HomeSysPowerRes(scenario, status, netGridKw, generatedKw, acConsumption, deliverableGeneratedKw, deliverableAcConsumption, deliverableGeneratedKw * inverter.dc2acEfficiency, predictedBatteryKw, overloaded, overgenerating);
    }
}
