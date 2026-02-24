namespace AiCon.Api.Settings;

public class BedrockSettings
{
    public const string SectionName = "Bedrock";

    /// <summary>AWS region where your Bedrock endpoint is available, e.g. "us-east-1"</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Bedrock model ID to invoke, e.g. "anthropic.claude-haiku-20240307-v1:0"</summary>
    public string ModelId { get; set; } = "anthropic.claude-haiku-20240307-v1:0";

    /// <summary>Maximum tokens the model is allowed to generate per request</summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Optional explicit AWS credentials.
    /// Leave empty in production — the SDK will fall back to IAM role or
    /// environment variables (AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY).
    /// For local dev, prefer ~/.aws/credentials or User Secrets.
    /// </summary>
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
}
