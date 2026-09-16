public class PowerReading
{
    public long Id {get;set;}
    public int? DeviceId {get;set;}
    public int? GridNodeId {get;set;}
    public required DateTime Timestamp { get; set; }
    public required double PowerKw { get; set; }
}


public class SpectralComponent
{
    public int Id { get; set; }
    public int PowerSpectrumId { get; set; }
    public PowerSpectrum PowerSpectrum { get; set; } = null!;

    public required double PeriodHours { get; set; }      // continuous, discovered — not an enum bucket
    public required double AmplitudeKw { get; set; }
    public required double PhaseRadians { get; set; }
    public required double SignificanceScore { get; set; } // amplitude / local noise floor — used to rank/select peaks
}


public class PowerSpectrum
{
    public int Id { get; set; }
    public int? DeviceId { get; set; }
    public Device? Device {get;set;}
    public int? GridNodeId { get; set; }
    public GridNode? GridNode {get;set;}
    public required DateTime ReferenceTimestamp { get; set; }
    public required double MeanPowerKw { get; set; }
    public ICollection<SpectralComponent> Components { get; set; } = new List<SpectralComponent>();
}
