using System.Text.Json;
using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using AiCon.Api.Models;
using AiCon.Api.Settings;
using Microsoft.Extensions.Options;

namespace AiCon.Api.Services;

/// <summary>
/// Sends flight change diffs to AWS Bedrock (Claude) and returns
/// a per-leg, human-readable English analysis with visual indicators.
/// </summary>
public class FlightChangeAnalyzer
{
    private readonly AmazonBedrockRuntimeClient _client;
    private readonly BedrockSettings _settings;
    private readonly ILogger<FlightChangeAnalyzer> _logger;

    public FlightChangeAnalyzer(IOptions<BedrockSettings> options, ILogger<FlightChangeAnalyzer> logger)
    {
        _settings = options.Value;
        _logger = logger;

        var region = RegionEndpoint.GetBySystemName(_settings.Region);

        _client = !string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? new AmazonBedrockRuntimeClient(new BedrockApiKeyCredentials(_settings.ApiKey), region)
            : !string.IsNullOrWhiteSpace(_settings.AccessKey) && !string.IsNullOrWhiteSpace(_settings.SecretKey)
                ? new AmazonBedrockRuntimeClient(new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey), region)
                : new AmazonBedrockRuntimeClient(region); // IAM role / env vars / ~/.aws/credentials
    }

    public async Task<IReadOnlyList<LegAnalysis>> AnalyzeAsync(List<FlightChange> changes)
    {
        if (changes.Count == 0)
            return [];

        _logger.LogInformation(
            "Sending {Count} flight change(s) to Bedrock model {Model}",
            changes.Count, _settings.ModelId);

        var request = BuildRequest(changes);
        var response = await _client.InvokeModelAsync(request);
        return ParseResponse(response, changes);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private InvokeModelRequest BuildRequest(List<FlightChange> changes)
    {
        var payload = new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = _settings.MaxTokens,
            messages = new[]
            {
                new { role = "user", content = BuildPrompt(changes) }
            }
        };

        return new InvokeModelRequest
        {
            ModelId = _settings.ModelId,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(payload))
        };
    }

    private static string BuildPrompt(List<FlightChange> changes)
    {
        // Build a compact, token-efficient change summary per leg
        var lines = changes.Select(c =>
        {
            var parts = new List<string>();

            if (c.PreviousAcRegNo != null || c.CurrentAcRegNo != null)
                parts.Add($"Aircraft Reg: {c.PreviousAcRegNo ?? "—"} → {c.CurrentAcRegNo ?? "—"}");

            if (c.PreviousCarrier != null || c.CurrentCarrier != null)
                parts.Add($"Carrier: {c.PreviousCarrier ?? "—"} → {c.CurrentCarrier ?? "—"}");

            return $"• LegId {c.LegId}: {string.Join(" | ", parts)}";
        });

        var changeBlock = string.Join("\n", lines);

        return $$"""
            You are a senior flight operations analyst. Analyze each flight leg change below.

            Rules:
            - Write in clear, professional English.
            - Use → to show old-to-new transitions.
            - Pick the most fitting emoji for the title:
                ✈  aircraft registration change
                🔄 carrier / airline change
                ⚠️ both aircraft and carrier changed
                ℹ️ informational / minor
            - Keep "title" to one concise line (max ~80 chars).
            - In "analysis" describe WHAT changed, and briefly note any typical operational implication
              (e.g. tail swap → possible maintenance swap or wet-lease; carrier change → codeshare or subcontractor switch).
            - If a value is "—" treat it as unknown/not provided.
            - Return ONLY a valid JSON array — no markdown, no extra text.

            Output schema (one object per LegId):
            [
              {
                "legId": "<string>",
                "title": "<emoji + short title>",
                "analysis": "<detailed English explanation>"
              }
            ]

            Flight changes to analyze:
            {{changeBlock}}
            """;
    }

    private IReadOnlyList<LegAnalysis> ParseResponse(InvokeModelResponse response, List<FlightChange> original)
    {
        try
        {
            using var doc = JsonDocument.Parse(response.Body);

            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString() ?? string.Empty;

            _logger.LogDebug("Bedrock raw response:\n{Response}", text);

            var results = JsonSerializer.Deserialize<List<LegAnalysis>>(
                text,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (results is { Count: > 0 })
                return results;

            _logger.LogWarning("Bedrock returned empty result list, using fallback.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Bedrock response, using fallback.");
        }

        return FallbackAnalysis(original);
    }

    private static IReadOnlyList<LegAnalysis> FallbackAnalysis(List<FlightChange> changes) =>
        changes.Select(c => new LegAnalysis
        {
            LegId = c.LegId,
            Title = "⚠️ Analysis unavailable",
            Analysis = "The AI response could not be parsed for this leg. Please retry."
        }).ToList();
}
