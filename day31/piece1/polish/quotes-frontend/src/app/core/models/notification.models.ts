// Mirrors QuotesApi.Modules.Notifications.Api.NotificationResponse exactly
// (NotificationEndpoints.cs GET /api/v1/notifications). Field names are the
// camelCase System.Text.Json writes them in.
//
// `id` is a GUID string, not a number — unlike Quote.id — because notifications
// are keyed by the Service Bus message that produced them.
// `quoteId` is null for any notification not tied to a specific quote.
export interface NotificationItem {
  id: string;
  type: string;
  message: string;
  quoteId: number | null;
  isRead: boolean;
  createdAtUtc: string;
  readAtUtc: string | null;
}
