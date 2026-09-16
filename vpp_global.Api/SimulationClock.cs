// Global, runtime-adjustable simulated clock. Replaces the old SimulationSpeed — that
// only exposed a rate (HoursPerTick), applied as a flat per-tick delta to accumulators,
// but Generator/Consumer/price curves need an actual absolute simulated timestamp to be
// evaluated at (they're a function of "when", not "how much time just passed"). Without
// this, HomeSysLogic was reading generator power via DateTime.UtcNow — true wall-clock
// time — completely decoupled from HoursPerTick, so speeding up the simulation sped up
// the displayed curves and battery SoC but not what HomeSysLogic itself perceived as
// "how much are we producing right now".
//
// Now() is derived from real elapsed time × rate (mirrors the frontend's simClockRef),
// not incremented per-tick — correct regardless of how often anything calls it.
// Rebased on every rate change so a mid-session speed change doesn't jump.
public class SimulationClock
{
    private readonly object _lock = new();
    private DateTime _simBase = DateTime.UtcNow;
    private DateTime _realBase = DateTime.UtcNow;
    private double _hoursPerTick = 1.0;

    public double HoursPerTick
    {
        get { lock (_lock) return _hoursPerTick; }
        set
        {
            lock (_lock)
            {
                _simBase = NowInternal();   // rebase using the rate still in effect
                _realBase = DateTime.UtcNow;
                _hoursPerTick = value;
            }
        }
    }

    public DateTime Now()
    {
        lock (_lock) return NowInternal();
    }

    private DateTime NowInternal()
    {
        var realElapsedSeconds = (DateTime.UtcNow - _realBase).TotalSeconds;
        return _simBase.AddHours(realElapsedSeconds * _hoursPerTick);
    }
}
