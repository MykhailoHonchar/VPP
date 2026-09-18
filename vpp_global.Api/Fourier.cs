using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;
using System.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Optimization;

public interface IPowerSpectrumAnalyzer
{
    Task Analyze(int deviceId, CancellationToken ct = default);
    Task AnalyzeGridNode(int gridNodeId, CancellationToken ct = default);
    Task<List<object>> GetRawSpectrum(int deviceId, CancellationToken ct = default);
    
}

public class PowerSpectrumAnalyzer : IPowerSpectrumAnalyzer
{

    public Task Analyze(int deviceId, CancellationToken ct = default) => AnalyzeAsync(deviceId, null, ct);
    public Task AnalyzeGridNode(int gridNodeId, CancellationToken ct = default) => AnalyzeAsync(null, gridNodeId, ct);

    private readonly VppDbContext _db;
    public PowerSpectrumAnalyzer(VppDbContext db) => _db = db;


    private async Task AnalyzeAsync(int? deviceId, int? gridNodeId, CancellationToken ct)
    {
        var readings = await _db.Set<PowerReading>()
            .Where(r => r.DeviceId == deviceId && r.GridNodeId == gridNodeId)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        if (readings.Count < 2) return;

        var t0 = readings[0].Timestamp;
        var (samples, hoursPerSample) = Resample(readings, t0);

        var mean = samples.Average();
        var windowed = ApplyHannWindow(samples, mean);

        var complex = windowed.Select(s => new Complex(s, 0)).ToArray();
        Fourier.Forward(complex, FourierOptions.Matlab);

        var peaks = PickSignificantPeaks(windowed, complex, hoursPerSample);

        var spectrum = new PowerSpectrum
        {
            DeviceId = deviceId,
            GridNodeId = gridNodeId,
            ReferenceTimestamp = t0,
            MeanPowerKw = mean,
            Components = peaks.Select(p => new SpectralComponent
            {
                PeriodHours = p.PeriodHours,
                AmplitudeKw = p.Amplitude,
                PhaseRadians = p.Phase,
                SignificanceScore = p.Score
            }).ToList()
        };

        _db.RemoveRange(_db.Set<PowerSpectrum>().Where(s => s.DeviceId == deviceId && s.GridNodeId == gridNodeId));
        _db.Add(spectrum);
        await _db.SaveChangesAsync(ct);
    }






    // Debug-only: dumps the raw FFT output for bins 0..n/2, before any peak-picking
    // or filtering happens, so you can see exactly what the transform produced.
    public async Task<List<object>> GetRawSpectrum(int deviceId, CancellationToken ct = default)
    {
        var readings = await _db.Set<PowerReading>()
            .Where(r => r.DeviceId == deviceId)
            .OrderBy(r => r.Timestamp)
            .ToListAsync(ct);

        if (readings.Count < 2) return new List<object>();

        var t0 = readings[0].Timestamp;
        var (samples, hoursPerSample) = Resample(readings, t0);

        var windowed = ApplyHannWindow(samples, samples.Average());
        var complex = windowed.Select(s => new Complex(s, 0)).ToArray();
        Fourier.Forward(complex, FourierOptions.Matlab);

        int n = complex.Length;
        var result = new List<object>();
        for (int k = 0; k <= n / 2; k++)
        {
            result.Add(new
            {
                Bin = k,
                PeriodHours = k == 0 ? (double?)null : (n * hoursPerSample) / k,
                Magnitude = complex[k].Magnitude,
                Phase = complex[k].Phase
            });
        }
        return result;
    }

    // Tapers the samples to zero at both edges before transforming. Without this, a
    // component that doesn't complete a whole number of cycles across the window (true
    // for almost any real period) creates a sharp discontinuity where the FFT's assumed
    // wraparound doesn't match — that leaks broadly across many low-frequency bins,
    // easily burying smaller genuine peaks under it. De-meaning first matters too: a
    // windowed non-zero mean stops being constant and injects its own broadband leakage.
    private static double[] ApplyHannWindow(double[] samples, double mean)
    {
        int n = samples.Length;
        var windowed = new double[n];
        for (int i = 0; i < n; i++)
        {
            double w = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (n - 1)));
            windowed[i] = (samples[i] - mean) * w;
        }
        return windowed;
    }

    private static (double[] samples, double hoursPerSample) Resample(List<PowerReading> readings, DateTime t0)
    {
        // TODO: real gap-filling onto a uniform grid — this still assumes readings are
        // roughly evenly spaced, just no longer that the spacing is exactly one hour.
        // Readings are recorded once per ingestion tick, at whatever simulated-time gap
        // that tick happened to advance by (SimulationClock's rate × real tick period,
        // itself jittery) — treating that as a hardcoded 1.0 made every recovered
        // PeriodHours wrong by roughly (true average spacing / 1.0), which is why a
        // predicted curve could come out oscillating at a completely different rate
        // than the real one. Deriving it from the actual first/last timestamps fixes the
        // scale even though the grid still isn't perfectly uniform tick to tick.
        var samples = readings.Select(r => r.PowerKw).ToArray();
        var totalHours = (readings[^1].Timestamp - readings[0].Timestamp).TotalHours;
        var hoursPerSample = samples.Length > 1 && totalHours > 0 ? totalHours / (samples.Length - 1) : 1.0;
        return (samples, hoursPerSample);
    }

    private static List<Peak> PickSignificantPeaks(double[] samples, Complex[] spectrum, double hoursPerSample)
    {
        // TODO: real noise-floor comparison. For now, naive top-3 bins by raw magnitude.
        int n = spectrum.Length;
        var candidates = new List<Peak>();

        for (int k = 1; k < n / 2; k++) // skip k=0 (that's the mean/DC term, handled separately)
        {
           double magnitude = spectrum[k].Magnitude;
            bool isLocalMax = magnitude >= spectrum[k - 1].Magnitude && magnitude >= spectrum[k + 1].Magnitude;
            if(!isLocalMax) continue;

         //   double periodHours = (n * hoursPerSample) / k;


            double alpha = spectrum[k - 1].Magnitude;
            double beta = spectrum[k].Magnitude;
            double gamma = spectrum[k + 1].Magnitude;
            double denom = alpha - 2 * beta + gamma;

            double delta = denom == 0 ? 0 : 0.5 * (alpha - gamma) / denom;
            double refinedBin = k + delta;

            double periodHours = (n * hoursPerSample) / refinedBin;

            // Re-measure amplitude and phase by correlating the original samples directly
            // against that exact refined frequency, instead of trusting bin k's own
            // Complex.Phase — which is only accurate when the true frequency lands exactly
            // on an integer bin.
            double omega = 2 * Math.PI * refinedBin / n;
            double sumCos = 0, sumSin = 0;
            for (int t = 0; t < n; t++)
            {
                sumCos += samples[t] * Math.Cos(omega * t);
                sumSin += samples[t] * Math.Sin(omega * t);
            }
            // The Hann window attenuates every bin's measured amplitude by its own average
            // value (its "coherent gain," 0.5) — correct for that here so AmplitudeKw
            // reflects the real physical signal, not the windowed one.
            const double hannCoherentGain = 0.5;
            double amplitude = (2.0 * Math.Sqrt(sumCos * sumCos + sumSin * sumSin) / n) / hannCoherentGain;
            double phase = Math.Atan2(-sumSin, sumCos); // same sign convention as Complex.Phase






         //   magnitude = magnitude*2/n;
            candidates.Add(new Peak(periodHours, amplitude, phase, amplitude));
        }

        return candidates.OrderByDescending(p => p.Score).Take(3).ToList();
    }

    private record Peak(double PeriodHours, double Amplitude, double Phase, double Score);



}


public static class PowerPredictor
{
    public static double Predict(PowerSpectrum spectrum, DateTime at)
    {
        var hoursSinceRef = (at - spectrum.ReferenceTimestamp).TotalHours;
        var value = spectrum.MeanPowerKw;
        foreach (var c in spectrum.Components)
            value += c.AmplitudeKw * Math.Cos(2 * Math.PI * hoursSinceRef / c.PeriodHours + c.PhaseRadians);
        

        switch (spectrum.Device)
        {
            case Generator:
                return Math.Max(0, value);
            case Consumer:
                return Math.Min(0, value);
            default:
                return value;
        }
    }
}