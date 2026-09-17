public abstract class Device
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public int HomeSystemId { get; set; }
    public HomeSystem HomeSystem { get; set; } = null!;
    // on Device, optional — a device may have 0 or 1 spectrum computed so far
    public PowerSpectrum? PowerSpectrum { get; set; }
    public ICollection<Simulation> Simulations{get;set;} = new List<Simulation>();
}


public enum InverterMode {Offgrid, Selling, Buying}
public record Curtail (double Gen, double Batt);
public class Inverter:Device
{
    public required double InverterCurrent{get;set;} = 0;
    public required int ModelId {get;set;}
    public required bool OnGrid {get;set;} = true;
    public required bool OnAcc {get;set;} = true;
    public required double MaxDCInput{get;set;} 
    public required double MaxOutputPower{get;set;}
    public required double ac2dcEfficiency{get;set;}
    public required double dc2acEfficiency{get;set;}
   // public required bool selling {get;set;} = false;
   // public required bool draining {get;set;} = false;
   public required InverterMode InverterMode{get;set;}
    public required InverterModel InverterModel{get;set;}


    public static Curtail valAndCurtail(double maxAC, double maxDC, double maxGen)
    {


        return new Curtail(1,1);
    }
}


public abstract class Generator : Device
{

    public required int ModelId {get;set;}
    public required GeneratorModel Model{get;set;}
}

public class WindPowerPlant : Generator
{
    public required double RotorDiameterM { get; set; }
    public required double CutInWindSpeedMs { get; set; }
}

public class SolarPowerPlant : Generator
{
    public required double TotalPanelAreaM2 { get; set; }
    public required int TiltDegrees { get; set; }
    public required int AzimuthDegrees { get; set; }
}
// Idle is deliberately unreachable from the automatic dispatch/ramp path — nothing in
// ComputeMode below ever returns it. It only exists for a future manual override to set
// explicitly; until that's built, no accumulator will ever actually show Idle.
public enum AccumulatorMode {Idle, Charging, Draining, Empty, Full}
public abstract class Accumulator : Device
{
    // Signed: positive = discharging/exporting, negative = charging/absorbing, 0 = idle.
    public required double TargetCurrentKw{get;set;} = 0;    // what HomeSysLogic decides this tick
    public required double AppliedCurrentKw{get;set;} = 0;   // the ramp-tracked, actually-applied value
    public required double MaxKWH {get;set;}
    public required double MinKWH {get;set;}
    public required double LowKWH {get;set;}
    public required double CurrentChargeKWH { get; set; }
    public required double CapacityKWH {get;set;}
    public DateTime? LastTickAt { get; set; }
    public AccumulatorMode Mode { get; set; } = AccumulatorMode.Idle;
    public required int Priority{get;set;}
    public required int ModelId {get;set;}
    public required AccumulatorModel Model{get;set;}

    // Ramps linearly from AppliedCurrentKw toward TargetCurrentKw at the model's
    // RampRateKwPerHour, evaluable at any timestamp — not just tick boundaries — so a
    // meter read at an arbitrary `at` returns a genuinely continuous value instead of a
    // value that only changes once per tick.
    public static double RampedCurrentAt(Accumulator acc, DateTime at)
    {
        if (acc.LastTickAt is null) return acc.TargetCurrentKw;
        double elapsedHours = Math.Max(0, (at - acc.LastTickAt.Value).TotalHours);
        double maxDelta = acc.Model.RampRateKwPerHour * elapsedHours;
        double diff = acc.TargetCurrentKw - acc.AppliedCurrentKw;
        return acc.AppliedCurrentKw + Math.Clamp(diff, -maxDelta, maxDelta);
    }

    // Empty/Full take priority over Charging/Draining whenever the current flow is the
    // one that's actually pinned against that bound — "still trying to drain but hit the
    // floor" is more informative than just "Draining". Idle is never returned here on
    // purpose: it's reserved for a manual override, so the automatic path preserves
    // whatever Mode already holds when current is zero and it isn't sitting at a bound.
    public static AccumulatorMode ComputeMode(Accumulator acc)
    {
        if (acc.AppliedCurrentKw > 0) return acc.CurrentChargeKWH <= acc.MinKWH ? AccumulatorMode.Empty : AccumulatorMode.Draining;
        if (acc.AppliedCurrentKw < 0) return acc.CurrentChargeKWH >= acc.MaxKWH ? AccumulatorMode.Full : AccumulatorMode.Charging;
        if (acc.CurrentChargeKWH <= acc.MinKWH) return AccumulatorMode.Empty;
        if (acc.CurrentChargeKWH >= acc.MaxKWH) return AccumulatorMode.Full;
        return acc.Mode;
    }
}

public class Battery : Accumulator
{
    //public int Priority{get;set;}
    public string? Chemistry { get; set; }
}

public class EV : Accumulator
{
    //public required bool IsConnected { get; set; } // plugged in and available for dispatch, vs. away/driving
  //  public required bool V2GCapable { get; set; }
    public string? LicensePlate { get; set; }
}

public class Consumer : Device
{
   // HVAC, appliance, lighting...

    public required int ModelId {get;set;}
    public required ConsumerModel Model{get;set;}
}