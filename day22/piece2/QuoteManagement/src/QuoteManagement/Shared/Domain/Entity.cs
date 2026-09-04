namespace QuoteManagement.Shared.Domain;

// Identity-based equality base for every module's aggregates/entities. Deliberately tiny —
// this is the kind of thing every module needs and none of them should redefine.
public abstract class Entity
{
    public Guid Id { get; protected set; }

    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        Id = id;
    }

    public override bool Equals(object? obj) =>
        obj is Entity other && other.GetType() == GetType() && other.Id == Id;

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);
}
