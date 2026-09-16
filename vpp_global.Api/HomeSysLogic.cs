using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

public class HomeSysLogic
{
    public record HomeSysPowerRes(int HomeSysId, DateTime TimeStamp, List<PowerReading> DevRes, double totalKw, double CurtailedKw, string Scenario, double NetGridKw, double GeneratedKw, double AcConsumption);
    private readonly VppDbContext db;
    private readonly IPowerMeterReader meter;
    private readonly IGridPriceProvider pricer;
    private readonly HomeSystem homeSystem;
    private readonly Inverter inverter;

    public static async Task<HomeSysLogic> CreateAsync(int homeSystemId, VppDbContext _db, IPowerMeterReader _meter, IGridPriceProvider _pricer, CancellationToken ct = default)
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

        return new HomeSysLogic(_db, _meter, _pricer, homeSystem, inverter);
    }

    private HomeSysLogic(VppDbContext _db, IPowerMeterReader _meter, IGridPriceProvider _pricer, HomeSystem _homeSystem, Inverter _inverter)
    {
        db = _db;
        meter = _meter;
        pricer = _pricer;
        homeSystem = _homeSystem;
        inverter = _inverter;
    }
    public async Task<HomeSysPowerRes> ReadHomeSysPowerAsync(int hsId, DateTime at, double hoursPerTick)
    {
        double generatedKw = 0;
        double acConsumption = 0;
        List<PowerReading> readings = new List<PowerReading>();

        // Uses the caller-provided `at` (the simulated clock), not DateTime.UtcNow —
        // this used to be shadowed by a locally-declared `now`, which meant Generator/
        // Consumer curves and price were always sampled at real wall-clock time no
        // matter how fast the simulation was sped up.
        async Task ReadDeviceAsync(int deviceId, bool isDcContributor, bool isAcConsumer)
        {
            var val = await meter.ReadPowerKwAt(deviceId, at);
            PowerReading reading = new PowerReading{ DeviceId = deviceId, Timestamp = at, PowerKw = val };
            readings.Add(reading);
            db.Add(reading);
            if (isDcContributor) generatedKw += val;
            if(isAcConsumer) acConsumption += -val;   // val is negative for Consumer; store demand as a positive kW magnitude
        }

        foreach (var g in homeSystem.Generators) await ReadDeviceAsync(g.Id, isDcContributor: true, isAcConsumer: false);
        foreach (var c in homeSystem.Consumers) await ReadDeviceAsync(c.Id, isDcContributor: false, isAcConsumer: true);

        if(acConsumption>inverter.MaxOutputPower)
        {
            //ACHTUNG: OVERLOAD!!!!!!!!!!!!!

            //idk, just limit or do something?
        }
        if(generatedKw>inverter.InverterModel.MaxDcInputKw)
        {
            //ACHTUNG: OVERGENERATION!!!!!!!!!!!!!

            //idk, just limit or do something?
        }
        var powerPrice = await pricer.GetPriceAt(homeSystem.GridNodeId, at);

//TEMP!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!
        int maxPriority = 2;


        Accumulator? aa = homeSystem.Batteries
            .Cast<Accumulator>()
            .Concat(homeSystem.EVs)
            .SingleOrDefault(a => a.Priority == homeSystem.ActiveAccPriority);


        Accumulator? firstAcc = homeSystem.Batteries
            .Cast<Accumulator>()
            .Concat(homeSystem.EVs)
            .SingleOrDefault(a => a.Priority == 0);

        Accumulator? lastAcc = homeSystem.Batteries
            .Cast<Accumulator>()
            .Concat(homeSystem.EVs)
            .SingleOrDefault(a => a.Priority == maxPriority);

        ///switch to next batt?
        Accumulator changeCell(bool next, Accumulator acc)
        {
            Accumulator? nextAA = homeSystem.Batteries
                .Cast<Accumulator>()
                .Concat(homeSystem.EVs)
                .SingleOrDefault(a => a.Priority == homeSystem.ActiveAccPriority + (next ? 1 : -1));

            if (nextAA is not null)
            {
                acc.targetCurrentKw = 0;   // stop commanding the outgoing cell — it's no longer active, or it'd stay flatlined at its last rate forever
                homeSystem.ActiveAccPriority += next ? 1 : -1;
                return nextAA;
            }
            return acc;
        }

        // Mode already reflects Empty/Full (computed and persisted at the end of last
        // tick's ramp loop below) — checking it directly here, instead of re-deriving the
        // same "pinned against the bound" condition from appliedCurrentKw/CurrentChargeKWH
        // again, keeps there being exactly one place that decides what Empty/Full means.
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
        double netGridKw = generatedKw*inverter.dc2acEfficiency-acConsumption;
        //if we produce enough to cover our demand


        if(netGridKw>0)
        {
            //is there batt?
            if(aa is null || firstAcc is null)
            {
                ///sell
                inverter.InverterMode = InverterMode.Selling;
                scenario = "Producing enough, no battery — Sell";
            }



            //is batt ok?
            else if(firstAcc.CurrentChargeKWH > firstAcc.lowKWH && (firstAcc.CurrentChargeKWH >= firstAcc.maxKWH || powerPrice > homeSystem.highPrice))
            {
                ///drain sell
                inverter.InverterMode = InverterMode.Selling;
                firstAcc.targetCurrentKw = 0;   // Idle: no charge/discharge rate, matches the ramp ticking down to 0
                scenario = "Producing enough, battery charged/price high — Idle & Sell";

            }
            //batt low
            else
            {
                ///charge offgrid
                inverter.InverterMode = InverterMode.Offgrid;
                aa.targetCurrentKw = -Math.Min(aa.Model.MaxChargeKw, netGridKw);   // negative = charging
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
                scenario = "Not enough production, no battery — Buy";
            }
            //is batt low?
            else if(lastAcc.CurrentChargeKWH < lastAcc.lowKWH &&(lastAcc.CurrentChargeKWH <= lastAcc.minKWH || powerPrice < homeSystem.lowPrice))
            {
                ///buy idle
                inverter.InverterMode = InverterMode.Buying;
                lastAcc.targetCurrentKw = 0;   // Idle: no charge/discharge rate
                scenario = "Not enough production, battery too low — Buy, battery Idle";
            }
            //batt ok
            else
            {
                ///Drain offgrid
                inverter.InverterMode = InverterMode.Offgrid;
                aa.targetCurrentKw = Math.Min(aa.Model.MaxDischargeKw, -netGridKw);   // positive = discharging
                scenario = "Not enough production, battery ok — Drain (offgrid)";
            }
        }


        inverter.InverterCurrent =
        inverter.InverterMode == InverterMode.Offgrid ?  0 :
            netGridKw > 0 ?
                netGridKw*inverter.dc2acEfficiency :
                    netGridKw*inverter.ac2dcEfficiency;

        // Ramp/SoC-advance runs LAST, after dispatch — not before — so a target changed
        // by dispatch THIS tick starts being ramped toward THIS tick, not next. Running
        // it first (the previous ordering) meant any new decision sat for a full tick
        // before the ramp even started reacting to it.
        foreach (var acc in homeSystem.Batteries.Cast<Accumulator>().Concat(homeSystem.EVs))
        {
            double oldApplied = acc.appliedCurrentKw;
            double newApplied = Accumulator.RampedCurrentAt(acc, at);
            acc.appliedCurrentKw = newApplied;

            double avgAppliedKw = (oldApplied + newApplied) / 2;   // trapezoidal — exact for a linear ramp
            acc.CurrentChargeKWH = Math.Clamp(acc.CurrentChargeKWH - avgAppliedKw * hoursPerTick, acc.minKWH, acc.maxKWH);
            acc.LastTickAt = at;
            acc.Mode = Accumulator.ComputeMode(acc);

            await ReadDeviceAsync(acc.Id, isDcContributor: false, isAcConsumer: false);
        }

        return new HomeSysPowerRes(hsId, at, readings, generatedKw, 0, scenario, netGridKw, generatedKw, acConsumption);


    }



}
