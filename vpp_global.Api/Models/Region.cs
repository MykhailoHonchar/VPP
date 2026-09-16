public class Region
{
    public required int Id {get;set;}
    public required string Name {get;set;}

    public ICollection<GridNode> GridNodes{get;set;} = new List<GridNode>();
  //  public ICollection<HomeSystem> HomeSystems{get;set;} = new List<HomeSystem>();
}