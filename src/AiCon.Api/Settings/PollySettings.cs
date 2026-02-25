namespace AiCon.Api.Settings;

public class PollySettings
{
    public const string SectionName = "Polly";

    /// <summary>AWS region where Polly is available, e.g. "eu-west-1"</summary>
    public string Region { get; set; } = "eu-west-1";

    /// <summary>
    /// Polly voice ID to use for synthesis.
    /// Defaults to "Amy" (en-GB, Neural) which is available in eu-west-1.
    /// See: https://docs.aws.amazon.com/polly/latest/dg/voicelist.html
    /// </summary>
    public string VoiceId { get; set; } = "Amy";

    /// <summary>
    /// Speech synthesis engine: "neural" (higher quality) or "standard".
    /// Neural voices require supported regions and voice IDs.
    /// </summary>
    public string Engine { get; set; } = "neural";

    /// <summary>
    /// Optional explicit AWS IAM credentials.
    /// Leave empty in production — the SDK will fall back to IAM role or
    /// environment variables (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY).
    /// </summary>
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}
