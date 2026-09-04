namespace QuoteManagement.Shared.Domain;

// An aggregate root is the only kind of object a module's application layer is allowed to
// load/save directly — everything inside it (value objects, child entities, invariants) is
// reached only through the aggregate's own methods. This base class just gives every
// aggregate a place to record what happened (domain events) while it enforces its rules.
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id) : base(id)
    {
    }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
