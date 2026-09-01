namespace ServiceBusDemo.Models;

/// <summary>
/// Standalone demo event for Day 19. This is intentionally NOT the QuotesApi domain model —
/// Day 19 must not depend on or reuse the existing QuotesApi project.
/// </summary>
public class QuoteEvent
{
    public int QuoteId { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}
