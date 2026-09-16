using MathNet.Numerics;
using Microsoft.EntityFrameworkCore;
using vpp_global.Api.Data;
using vpp_global.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddOpenApi();
builder.Services.AddDbContext<VppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<IPowerMeterReader, SimulatedPowerMeterReader>();
builder.Services.AddScoped<IGridPriceProvider, SimulatedGrid>();
builder.Services.AddScoped<IPowerSpectrumAnalyzer, PowerSpectrumAnalyzer>();
builder.Services.AddSingleton<SimulationClock>();
builder.Services.AddSingleton<HomeSysStatusTracker>();
builder.Services.AddHostedService<PowerReadingIngestionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapDeviceEndpoints();
app.MapGridEndpoints();
app.MapHomeSystemEndpoints();
app.MapSimulationEndpoints();

app.Run();
