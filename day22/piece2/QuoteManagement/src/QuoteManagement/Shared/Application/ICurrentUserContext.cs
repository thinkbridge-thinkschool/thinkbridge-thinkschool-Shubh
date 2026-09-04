namespace QuoteManagement.Shared.Application;

// Identity owns authentication and provides the real implementation of this; every other
// module (Quotes included) depends only on this interface to learn who is calling, so
// nobody but Identity needs to know how a caller was authenticated (headers today, JWT
// bearer tokens in a full implementation).
public interface ICurrentUserContext
{
    Guid UserId { get; }
    string DisplayName { get; }
}
