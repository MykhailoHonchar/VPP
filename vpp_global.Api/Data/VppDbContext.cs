using Microsoft.EntityFrameworkCore;

namespace vpp_global.Api.Data;

public class VppDbContext : DbContext
{
    public VppDbContext(DbContextOptions<VppDbContext> options) : base(options)
    {
    }

    public DbSet<Region> Regions { get; set; }
    public DbSet<GridNode> GridNodes { get; set; }
    public DbSet<HomeSystem> HomeSystems { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<PowerReading> PowerReadings { get; set; }
    public DbSet<PowerSpectrum> PowerSpectra { get; set; }
    public DbSet<SpectralComponent> SpectralComponents { get; set; }
    public DbSet<WindPowerPlant> WindPowerPlants { get; set; }
    public DbSet<SolarPowerPlant> SolarPowerPlants { get; set; }
    public DbSet<Battery> Batteries { get; set; }
    public DbSet<EV> EVs { get; set; }
    public DbSet<Consumer> Consumers { get; set; }
    public DbSet<Inverter> Inverters { get; set; }
    public DbSet<InverterModel> InverterModels { get; set; }
    public DbSet<Simulation> Simulations { get; set; }


    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Device>()
            .HasOne(d=>d.PowerSpectrum)
            .WithOne(ps=>ps.Device)
            .HasForeignKey<PowerSpectrum>(ps=>ps.DeviceId);

        mb.Entity<GridNode>()
            .HasOne(g=>g.PowerSpectrum)
            .WithOne(ps=>ps.GridNode)
            .HasForeignKey<PowerSpectrum>(ps=>ps.GridNodeId);

        // Generator/Accumulator/Consumer/Inverter each declare a "ModelId"/"Model"-named
        // relationship on an abstract TPH base — without this, EF can't tell these apart
        // from each other in the shared Devices table and scatters them into confusing
        // per-leaf-type shadow columns instead of one clean column per category.
        mb.Entity<Generator>()
            .HasOne(g => g.Model)
            .WithMany()
            .HasForeignKey(g => g.ModelId);

        mb.Entity<Accumulator>()
            .HasOne(a => a.Model)
            .WithMany()
            .HasForeignKey(a => a.ModelId);

        mb.Entity<Consumer>()
            .HasOne(c => c.Model)
            .WithMany()
            .HasForeignKey(c => c.ModelId);

        mb.Entity<Inverter>()
            .HasOne(i => i.InverterModel)
            .WithMany()
            .HasForeignKey(i => i.ModelId);

        mb.Entity<Simulation>()
            .HasOne(s=>s.Device)
            .WithMany(d=>d.Simulations)
            .HasForeignKey(s=>s.DeviceId);


        mb.Entity<Simulation>()
            .HasOne(s=>s.GridNode)
            .WithMany(g=>g.Simulations)
            .HasForeignKey(s=>s.GridNodeId);


        // Inverter is a 1:1 relationship conceptually, but TPH puts every device type in
        // one physical Devices table — a real EF HasOne/WithOne here would force a
        // table-wide unique constraint on HomeSystemId, which breaks the moment any other
        // device type (Generator, Battery, ...) shares that same HomeSystemId. So it's
        // deliberately left unmapped; HomeSysLogic queries Inverter with its own separate
        // lookup instead of an .Include().
        mb.Entity<HomeSystem>().Ignore(hs => hs.Inverter);

        mb.Entity<HomeSystem>()
            .HasMany(hs => hs.Generators)
            .WithOne(d => d.HomeSystem)
            .HasForeignKey(d => d.HomeSystemId);

        mb.Entity<HomeSystem>()
            .HasMany(hs => hs.Batteries)
            .WithOne(d => d.HomeSystem)
            .HasForeignKey(d => d.HomeSystemId);

        mb.Entity<HomeSystem>()
            .HasMany(hs => hs.EVs)
            .WithOne(d => d.HomeSystem)
            .HasForeignKey(d => d.HomeSystemId);

        mb.Entity<HomeSystem>()
            .HasMany(hs => hs.Consumers)
            .WithOne(d => d.HomeSystem)
            .HasForeignKey(d => d.HomeSystemId);
    }

}
