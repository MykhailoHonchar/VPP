


public class HomeSystem
{
    public required int Id {get;set;}
    public required string Name {get;set;}
    public required int GridNodeId{get;set;}
    public required GridNode GridNode{get;set;}

    public required double lowPrice{get;set;}
    public required double highPrice{get;set;}

    // Which accumulator priority is currently active for charge/discharge dispatch.
    // Must be stored here, not on HomeSysLogic — that class is recreated fresh every
    // tick (see Ingestion.cs), so a field on it can never survive past the tick that
    // set it; any switch decision would be silently undone the moment the next tick
    // starts back at 0.
    public int ActiveAccPriority { get; set; } = 0;

    public Inverter? Inverter {get;set;}
    public ICollection<Generator> Generators{get;set;} = new List<Generator>();
    public ICollection<Consumer> Consumers{get;set;} = new List<Consumer>();
    public ICollection<Battery> Batteries{get;set;} = new List<Battery>();
    public ICollection<EV> EVs{get;set;} = new List<EV>();
}