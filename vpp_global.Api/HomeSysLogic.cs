using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;

// A stable, colorable key for "why is the inverter doing what it's doing right now" — the
// free-text Scenario string is for humans; this is for the UI to key a color off of without
// string-matching. Sell/Buy each split into two reasons because they're triggered by two
// independent OR'd conditions (battery bound vs price threshold) that the UI wants to tell
// apart, even though today's dispatch logic only ever picks one InverterMode for both.
public enum DispatchStatus { SellBatteryFull, SellHighPrice, SellNoBattery, BuyBatteryEmpty, BuyLowPrice, BuyNoBattery, ChargeOffgrid, DrainOffgrid }

public class HomeSysLogic
{
    // Trimmed to the fields Ingestion.cs (the only caller) actually reads. It used to also
    // carry HomeSysId/TimeStamp/DevRes (the caller already has `hsId`/`now`, and every
    // PowerReading is persisted via db.Add regardless of whether it's returned here) plus
    // totalKw (always just a copy of GeneratedKw) and CurtailedKw (always hardcoded 0).
    // GeneratedKw/AcConsumption are the true measured ("pure") values, uncurtailed.
    // DeliverableGeneratedKw/DeliverableAcConsumption are those same two quantities after
    // clamping to the inverter's MaxDCInput/MaxOutputPower — still in their native DC/AC
    // domains respectively, for comparing directly against the raw per-device curves.
    // ActualGenerationKw is DeliverableGeneratedKw converted to AC (DeliverableGeneratedKw
    // * dc2acEfficiency) — a third, separate quantity, not a duplicate of either.
    // PredictedBatteryKw is the forecast net battery power — consumption's predicted
    // magnitude minus generation's, so positive means predicted discharge and negative
    // predicted charge, matching Accumulator's own sign convention. Null whenever a
    // Generator or Consumer on this HomeSystem hasn't been analyzed yet (no PowerSpectrum
    // to predict from).
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

        // Predicted net battery power, computed here (not just derived client-side) so any
        // future dispatch logic can use it directly. Sums every Generator's and every
        // Consumer's predicted curve (matching how generatedKw/acConsumption themselves
        // aggregate across multiple devices), then inverts generation-plus-consumption
        // (consumption's own predicted value is already negative) so positive = predicted
        // discharge, negative = predicted charge — the same convention AppliedCurrentKw
        // uses. Null unless every Generator and Consumer on this HomeSystem has a
        // PowerSpectrum to predict from (i.e. /analyze has actually been run on all of them).
        double? predictedBatteryKw = null;
        var predictableDeviceIds = homeSystem.Generators.Select(g => g.Id)
            .Concat(homeSystem.Consumers.Select(c => c.Id))
            .ToList();
        if (predictableDeviceIds.Count > 0)
        {
            var spectra = await db.Set<PowerSpectrum>()
                .Include(s => s.Components)
                .Include(s => s.Device)
                .Where(s => s.DeviceId != null && predictableDeviceIds.Contains(s.DeviceId.Value))
                .ToListAsync(ct);
            if (spectra.Count == predictableDeviceIds.Count)
                predictedBatteryKw = -spectra.Sum(s => PowerPredictor.Predict(s, at));
        }

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
