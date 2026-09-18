using System.Collections.Concurrent;

// Holds the most recent HomeSysLogic decision output per home system — the single
// source of truth for what the UI shows. Nothing downstream (the status endpoint)
// re-derives these numbers independently; it only reads what HomeSysLogic actually
// decided. Registered as a singleton, written by PowerReadingIngestionService each tick.
public record HomeSysSnapshot(string Scenario, DispatchStatus Status, double NetGridKw, double GeneratedKw, double AcConsumption, double DeliverableGeneratedKw, double DeliverableAcConsumption, double ActualGenerationKw, double? PredictedBatteryKw, bool Overloaded, bool Overgenerating, DateTime At);

public class HomeSysStatusTracker
{
    private readonly ConcurrentDictionary<int, HomeSysSnapshot> _snapshots = new();

    public void Set(int homeSystemId, HomeSysSnapshot snapshot) => _snapshots[homeSystemId] = snapshot;

    public HomeSysSnapshot? Get(int homeSystemId) => _snapshots.TryGetValue(homeSystemId, out var s) ? s : null;
}
