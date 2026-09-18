// Global, runtime-adjustable switch for whether the ingestion tick actually persists
// PowerReading rows. Defaults to off: with it always-on, every casual speed change or
// test session mixes into the same per-device history at whatever sampling rate that
// session happened to run at, which pollutes it for anything (like /analyze) that later
// treats it as one consistent series. Recording is now something you turn on
// deliberately, for a clean stretch you actually want analyzed.
public class RecordingSettings
{
    public bool RecordPowerReadings { get; set; } = false;
}
