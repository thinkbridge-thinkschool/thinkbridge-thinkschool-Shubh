namespace QuoteManagement.Shared.Domain;

// Marker for an in-module domain event — something that happened inside one aggregate,
// relevant to that module's own application layer (e.g. for audit/logging). NOT the same
// thing as an integration event (Shared.Application.EventBus): a domain event never
// crosses a module boundary; an integration event is the explicit, versioned contract that
// does.
public interface IDomainEvent
{
}
