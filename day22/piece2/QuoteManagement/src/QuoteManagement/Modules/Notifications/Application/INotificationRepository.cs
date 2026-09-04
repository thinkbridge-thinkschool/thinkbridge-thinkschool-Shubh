using QuoteManagement.Modules.Notifications.Domain;

namespace QuoteManagement.Modules.Notifications.Application;

internal interface INotificationRepository
{
    void Add(Notification notification);
    Task<IReadOnlyList<Notification>> GetAllAsync(CancellationToken cancellationToken);
}
