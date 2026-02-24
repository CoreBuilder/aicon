using AiCon.Api.Models;
using AiCon.Api.Services;
using AiCon.Api.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<BedrockSettings>(
    builder.Configuration.GetSection(BedrockSettings.SectionName));

builder.Services.AddSingleton<FlightChangeAnalyzer>();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => "Hello this is an api with .net 10 and aspire framework");

app.MapPost("/analyz", async (List<FlightChange> changes, FlightChangeAnalyzer analyzer) =>
{
    if (changes is null || changes.Count == 0)
        return Results.BadRequest(new { error = "changes list cannot be empty" });

    var results = await analyzer.AnalyzeAsync(changes);
    return Results.Ok(results);
})
.WithName("AnalyzFlightChanges")
.WithSummary("Analyzes flight leg changes using AWS Bedrock (Claude) and returns per-leg human-readable summaries.");

app.Run();
