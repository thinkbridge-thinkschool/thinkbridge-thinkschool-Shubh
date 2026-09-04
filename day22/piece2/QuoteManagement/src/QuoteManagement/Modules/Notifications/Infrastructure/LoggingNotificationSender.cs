using Microsoft.Extensions.Logging;
using QuoteManagement.Modules.Notifications.Application;
using QuoteManagement.Modules.Notifications.Domain;

namespace QuoteManagement.Modules.Notifications.Infrastructure;

// Placeholder delivery mechanism — logs instead of actually emailing/pushing. A real
// implementation (SendGrid, SES, a push provider...) would satisfy the same
// INotificationSender interface, so nothing else in this module would need to change.
internal sealed class LoggingNotificationSender(ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(Notification notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "[Notifications] Sending notification {NotificationId} to user {RecipientUserId}: {Message}",
            notification.Id, notification.RecipientUserId, notification.Message);
        return Task.CompletedTask;
    }
}
