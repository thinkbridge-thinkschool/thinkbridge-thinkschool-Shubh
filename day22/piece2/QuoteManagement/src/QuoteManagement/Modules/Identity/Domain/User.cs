using QuoteManagement.Shared.Domain;

namespace QuoteManagement.Modules.Identity.Domain;

// Identity's own concept of a user. Other modules never see this type — they only ever
// see a Guid UserId (via ICurrentUserContext, Shared.Application) and treat it as an
// opaque identifier. This keeps Quotes/Notifications from needing to know anything about
// how authentication or user profiles work.
internal sealed class User : Entity
{
    public string DisplayName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    private User()
    {
    }

    public User(Guid id, string displayName, string email) : base(id)
    {
        DisplayName = displayName;
        Email = email;
    }
}
