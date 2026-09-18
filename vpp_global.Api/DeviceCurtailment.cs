// Shared curtailment logic — a Generator's DC output is capped by the home system's
// inverter's MaxDCInput, a Consumer's AC draw by its MaxOutputPower. Used everywhere a
// device's power reading turns into something persisted or displayed as "the" value for
// that device (the live chart, backfilled history, and live-recorded PowerReadings), so
// they never disagree about what's actually deliverable — a PowerSpectrum computed from
// PowerReadings and a live /devices/{id}/live read describe the same clamped signal.
// Only correct today because there's exactly one Generator and one Consumer per
// HomeSystem — with more than one of either, the inverter's limit applies to their sum,
// not to each independently.
public static class DeviceCurtailment
{
    public static double Apply(Device device, Inverter inverter, double kw) =>
        device switch
        {
            Generator => Math.Min(kw, inverter.MaxDCInput),
            Consumer => Math.Max(kw, -inverter.MaxOutputPower),   // Consumer reads negative
            _ => kw,
        };
}
