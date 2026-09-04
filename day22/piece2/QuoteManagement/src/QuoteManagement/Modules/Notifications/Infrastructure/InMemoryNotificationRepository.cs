using QuoteManagement.Modules.Notifications.Application;
using QuoteManagement.Modules.Notifications.Domain;

namespace QuoteManagement.Modules.Notifications.Infrastructure;

// In-memory placeholder standing in for a real notifications table. Registered as a
// singleton so notifications survive across the scoped requests/background-service scopes
// that write to it during this scaffold's demo run.
internal sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly List<Notification> _notifications = [];
    private readonly Lock _gate = new();

    public void Add(Notification notification)
    {
        lock (_gate)
        {
            _notifications.Add(notification);
        }
    }

    public Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Notification>>(_notifications.ToList());
        }
    }
}
