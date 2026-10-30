using Game.Api.Contracts;
using Game.Api.Data;
using Game.Api.Errors;
using Game.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GameDb") ?? "Data Source=game.db"));
builder.Services.AddSingleton(MockContentLoader.Load());
builder.Services.AddSingleton<MockDialogueProvider>();
builder.Services.AddSingleton<IDialogueProvider>(services => services.GetRequiredService<MockDialogueProvider>());
builder.Services.Configure<OpenAiOptions>(builder.Configuration.GetSection("OpenAI"));
builder.Services.AddHttpClient<OpenAiDialogueProvider>(client =>
    client.BaseAddress = new Uri("https://api.openai.com/v1/"));
builder.Services.AddTransient<IDialogueProvider>(services => services.GetRequiredService<OpenAiDialogueProvider>());
builder.Services.AddScoped<DialogueGenerationService>();
builder.Services.AddSingleton<MockCareerEndingProvider>();
builder.Services.AddSingleton<ICareerEndingProvider>(services => services.GetRequiredService<MockCareerEndingProvider>());
builder.Services.AddHttpClient<OpenAiCareerEndingProvider>(client =>
    client.BaseAddress = new Uri("https://api.openai.com/v1/"));
builder.Services.AddTransient<ICareerEndingProvider>(services => services.GetRequiredService<OpenAiCareerEndingProvider>());
builder.Services.AddScoped<CareerEndingGenerationService>();
builder.Services.AddScoped<MockGameService>();

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, code, message, retryable) = exception switch
    {
        GameRuleException error => (400, error.Code, error.Message, false),
        SessionNotFoundException => (404, "SESSION_NOT_FOUND", "세션을 찾을 수 없습니다.", false),
        _ => (500, "INTERNAL_ERROR", "요청을 처리하지 못했습니다.", true)
    };
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new ErrorEnvelope(
        new ErrorResponse(code, message, retryable, context.TraceIdentifier)));
}));

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<GameDbContext>().Database.EnsureCreated();
}

app.MapGet("/health", async (GameDbContext database) => Results.Ok(new HealthResponse(
    "ok", "game-api", "0.2.0", await database.Database.CanConnectAsync())));
app.MapPost("/api/v1/sessions", async (SessionStartRequest request, MockGameService game) =>
    Results.Ok(await game.StartSessionAsync(request.Mode, request.FailureScenario)));
app.MapGet("/api/v1/sessions/{sessionId:guid}", async (Guid sessionId, MockGameService game) =>
    Results.Ok(await game.GetSessionAsync(sessionId)));
app.MapPost("/api/v1/sessions/{sessionId:guid}/dialogues", async (Guid sessionId, DialogueRequest request, MockGameService game) =>
    Results.Ok(await game.SubmitDialogueAsync(sessionId, request)));
app.MapGet("/api/v1/sessions/{sessionId:guid}/choices", async (Guid sessionId, MockGameService game) =>
    Results.Ok(await game.GetChoicesAsync(sessionId)));
app.MapPost("/api/v1/sessions/{sessionId:guid}/choices", async (Guid sessionId, ChoiceRequest request, MockGameService game) =>
    Results.Ok(await game.SubmitChoiceAsync(sessionId, request)));
app.MapPost("/api/v1/sessions/{sessionId:guid}/farming", async (Guid sessionId, FarmingResultRequest request, MockGameService game) =>
    Results.Ok(await game.SubmitFarmingAsync(sessionId, request)));
app.MapPost("/api/v1/sessions/{sessionId:guid}/farming-progress", async (Guid sessionId, FarmingProgressRequest request, MockGameService game) =>
    Results.Ok(await game.SaveFarmingProgressAsync(sessionId, request)));

app.Run();
public partial class Program;
