
public class GridNode
{
    public required int Id {get;set;}
    public required string Name {get;set;}
    public required int RegionId{get;set;}
    public required Region Region{get;set;}
    public PowerSpectrum? PowerSpectrum { get; set; }
    public ICollection<Simulation> Simulations{get;set;} = new List<Simulation>();
    public ICollection<HomeSystem> HomeSystems{get;set;} = new List<HomeSystem>();


}