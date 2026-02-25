using AiCon.Api.Settings;
using Amazon;
using Amazon.Polly;
using Amazon.Polly.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

namespace AiCon.Api.Services;

/// <summary>
/// Converts text to speech using AWS Polly and returns an MP3 audio stream.
/// The stream can be returned directly to the HTTP response — no disk writes required.
/// </summary>
public class TextToSpeechService
{
    private readonly AmazonPollyClient _client;
    private readonly PollySettings _settings;
    private readonly ILogger<TextToSpeechService> _logger;

    public TextToSpeechService(IOptions<PollySettings> options, ILogger<TextToSpeechService> logger)
    {
        _settings = options.Value;
        _logger = logger;

        var region = RegionEndpoint.GetBySystemName(_settings.Region);

        _client = !string.IsNullOrWhiteSpace(_settings.AccessKey) && !string.IsNullOrWhiteSpace(_settings.SecretKey)
            ? new AmazonPollyClient(new BasicAWSCredentials(_settings.AccessKey, _settings.SecretKey), region)
            : new AmazonPollyClient(region); // IAM role / env vars / ~/.aws/credentials
    }

    /// <summary>
    /// Synthesizes the given text into an MP3 audio stream via AWS Polly.
    /// </summary>
    /// <param name="text">Plain text to synthesize (max ~3000 characters for Polly).</param>
    /// <param name="voiceId">Optional voice override; falls back to <see cref="PollySettings.VoiceId"/>.</param>
    /// <returns>MP3 audio stream — must be copied to the HTTP response before disposal.</returns>
    public async Task<Stream> SynthesizeAsync(string text, string? voiceId = null)
    {
        var voice = voiceId ?? _settings.VoiceId;
        var engine = _settings.Engine.ToLowerInvariant() == "standard" ? Engine.Standard : Engine.Neural;

        _logger.LogInformation(
            "Synthesizing {Length} chars with Polly voice={Voice} engine={Engine}",
            text.Length, voice, engine);

        var request = new SynthesizeSpeechRequest
        {
            Text = text,
            VoiceId = voice,
            OutputFormat = OutputFormat.Mp3,
            Engine = engine
        };

        var response = await _client.SynthesizeSpeechAsync(request);
        return response.AudioStream;
    }
}
