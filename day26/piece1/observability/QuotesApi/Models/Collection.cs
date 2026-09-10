namespace QuotesApi.Models;

public class Collection
{
    private readonly List<CollectionItem> _items = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int OwnerId { get; private set; }

    public IReadOnlyCollection<CollectionItem> Items => _items.AsReadOnly();

    private Collection()
    {
    }
    public Collection(string name, int ownerId)
    {
        SetName(name);
        OwnerId = ownerId;
    }

    public void AddItem(int quoteId, DateTimeOffset addedAt)
    {
        if (quoteId <= 0)
            throw new ArgumentException("QuoteId must be greater than zero.");

        if (_items.Count >= 50)
            throw new InvalidOperationException("A collection can contain at most 50 items.");

        if (_items.Any(x => x.QuoteId == quoteId))
            throw new InvalidOperationException("Quote already exists in the collection.");

        _items.Add(new CollectionItem(quoteId, addedAt.UtcDateTime));
    }
     public void RemoveItem(int quoteId)
{
    var item = _items.FirstOrDefault(x => x.QuoteId == quoteId);

    if (item is null)
        throw new InvalidOperationException(
            "Quote does not exist in the collection.");

    _items.Remove(item);
}
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Trim().Length < 3 ||
            name.Trim().Length > 80)
        {
            throw new ArgumentException(
                "Collection name must be between 3 and 80 characters.");
        }

        Name = name.Trim();
    }
}