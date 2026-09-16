public class Simulation
{
    public int Id {get;set;}
    public required double MeanKw {get;set;}
    public required double AmplitudeKw {get;set;}
    public required double PeriodHours {get;set;}
    public required double NoiseStdDevKw {get;set;}
    public required double PhaseShift {get;set;}
    public bool AllowNegative{get;set;}

    public int? DeviceId { get; set; }
    public Device? Device { get; set; }
    public int? GridNodeId { get; set; }
    public GridNode? GridNode { get; set; }
}