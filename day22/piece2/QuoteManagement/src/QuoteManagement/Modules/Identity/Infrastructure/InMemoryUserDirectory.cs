using QuoteManagement.Modules.Identity.Application;
using QuoteManagement.Modules.Identity.Domain;

namespace QuoteManagement.Modules.Identity.Infrastructure;

// Infrastructure placeholder: a real implementation would query a database or an external
// identity provider. Seeded with one demo user so the scaffold has something to return.
internal sealed class InMemoryUserDirectory : IUserDirectory
{
    public static readonly Guid DemoUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly Dictionary<Guid, User> _users = new()
    {
        [DemoUserId] = new User(DemoUserId, "Demo User", "demo.user@example.com")
    };

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_users.GetValueOrDefault(id));
}
