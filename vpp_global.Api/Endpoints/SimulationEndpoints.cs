namespace vpp_global.Api.Endpoints;

public static class SimulationEndpoints
{
    public static void MapSimulationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/simulation/speed", (SimulationClock clock) =>
            Results.Ok(new { clock.HoursPerTick }));

        app.MapPost("/simulation/speed", (SetSimulationSpeedRequest request, SimulationClock clock) =>
        {
            clock.HoursPerTick = request.HoursPerTick;   // setter rebases the clock internally
            return Results.Ok(new { clock.HoursPerTick });
        });
    }
}

public record SetSimulationSpeedRequest(double HoursPerTick);
