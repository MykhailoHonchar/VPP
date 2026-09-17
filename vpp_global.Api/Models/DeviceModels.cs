public abstract class DeviceModel
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Manufacturer { get; set; }
}

public class InverterModel : DeviceModel
{
    public required double RatedPowerKw { get; set; }       // max continuous AC output
    // Unused for now — Inverter.MaxDCInput (the instance-level field) is the one HomeSysLogic
    // and /devices/{id}/live actually clamp against, since the two had drifted to different
    // values in practice. Left here rather than deleted in case a real per-model spec (as
    // opposed to a per-instance override) is wanted later.
    public required double MaxDcInputKw { get; set; }        // max DC power it can accept from panels/battery (usually somewhat higher than RatedPowerKw)
    public required double EfficiencyPercent { get; set; }   // DC-to-AC conversion efficiency, typically 95-98 for modern inverters
    public required bool IsHybrid { get; set; }               // can it manage battery charge/discharge directly, vs. a grid-tie solar-only inverter
}

public class GeneratorModel : DeviceModel
{
    public required double RatedCapacityKw { get; set; }     // nameplate max continuous output — moved here from Generator since it's fixed by the model, not the individual unit
    public required double EfficiencyPercent { get; set; }   // energy-conversion efficiency (mechanical-to-electrical for wind, cell efficiency for solar)
}

public class AccumulatorModel : DeviceModel
{
    public required double CapacityKwh { get; set; }
    public required double MaxChargeKw { get; set; }
    public required double MaxDischargeKw { get; set; }
    public required double RampRateKwPerHour { get; set; }   // max rate of change of applied current, kW per simulated hour
    public required double RoundTripEfficiencyPercent { get; set; } // energy lost over one full charge+discharge cycle, typically 85-95 — directly affects VPP arbitrage economics
    public required double MaxDepthOfDischargePercent { get; set; } // how far it can safely be drained (e.g. 90 means don't go below 10% SOC)
    public required int RatedCycleLife { get; set; }                 // charge/discharge cycles before meaningful capacity degradation
    public required string Chemistry { get; set; }                    // e.g. LiFePO4, NMC — fixed by the model, applies to EV batteries too, not just stationary Battery
}

public class ConsumerModel : DeviceModel
{
    public required double RatedPowerKw { get; set; }
    public required string Category { get; set; }        // e.g. HVAC, appliance, lighting
    public required bool IsControllable { get; set; }    // can the VPP remotely curtail/shift this load, or is it a fixed always-on draw
}
