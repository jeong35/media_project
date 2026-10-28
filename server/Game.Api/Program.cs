var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new HealthResponse(
    Status: "ok",
    Service: "game-api",
    Version: "0.1.0")));

app.Run();

public sealed record HealthResponse(string Status, string Service, string Version);

