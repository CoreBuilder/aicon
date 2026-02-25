using AiCon.Api.Models;
using AiCon.Api.Services;
using AiCon.Api.Settings;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<BedrockSettings>(
    builder.Configuration.GetSection(BedrockSettings.SectionName));

builder.Services.Configure<PollySettings>(
    builder.Configuration.GetSection(PollySettings.SectionName));

builder.Services.AddSingleton<FlightChangeAnalyzer>();
builder.Services.AddSingleton<TextToSpeechService>();

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

app.MapPost("/speak", async (SpeakRequest request, TextToSpeechService tts) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new { error = "text cannot be empty" });

    var audioStream = await tts.SynthesizeAsync(request.Text, request.VoiceId);
    return Results.Stream(audioStream, "audio/mpeg");
})
.WithName("SpeakText")
.WithSummary("Converts text to speech via AWS Polly and streams back an MP3 audio binary (no file saved).");

app.Run();
