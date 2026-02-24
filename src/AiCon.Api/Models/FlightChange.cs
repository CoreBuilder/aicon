namespace AiCon.Api.Models;

public class FlightChange
{
    public string LegId { get; set; } = string.Empty;
    public string? PreviousAcRegNo { get; set; }
    public string? CurrentAcRegNo { get; set; }
    public string? PreviousCarrier { get; set; }
    public string? CurrentCarrier { get; set; }
}
