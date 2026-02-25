namespace AiCon.Api.Models;

public class SpeakRequest
{
    /// <summary>The text to synthesize into speech.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Polly voice ID (e.g. "Amy", "Joanna", "Matthew").
    /// If not provided, the value from PollySettings is used.
    /// </summary>
    public string? VoiceId { get; set; }
}
