namespace QuoteManagement.Shared.Application.EventBus;

// The public boundary between modules. A module publishes one of these when something
// happened that another module might legitimately care about; it never hands the other
// module its domain entities or internal types directly.
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOnUtc { get; }
}
