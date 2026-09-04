using QuoteManagement.Modules.Identity.Domain;

namespace QuoteManagement.Modules.Identity.Application;

// Application-layer placeholder for user lookup — stands in for what would normally be
// backed by a real user store (or an external IdP). Not fully wired up in this scaffold;
// present to show where Identity's own application logic would live.
internal interface IUserDirectory
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
