namespace AiCon.Api.Models;

public class LegAnalysis
{
    public string LegId { get; set; } = string.Empty;

    /// <summary>
    /// Short one-line title with emoji indicating the type of change.
    /// e.g. "✈ Aircraft swap: TC-JFG → TC-KLM"
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed human-readable English analysis of what changed
    /// and its potential operational impact.
    /// </summary>
    public string Analysis { get; set; } = string.Empty;
}
