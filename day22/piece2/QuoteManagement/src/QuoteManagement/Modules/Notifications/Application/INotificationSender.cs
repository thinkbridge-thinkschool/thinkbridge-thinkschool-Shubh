using QuoteManagement.Modules.Notifications.Domain;

namespace QuoteManagement.Modules.Notifications.Application;

// Abstraction over however a notification is actually delivered (email, push, in-app...).
// The handler below depends only on this; Infrastructure supplies the real delivery
// mechanism (a logging placeholder in this scaffold).
internal interface INotificationSender
{
    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}
