namespace QuotesApi.Modules.Quotes.Application;

// Deliberately has no OwnerId: binding the Collection domain entity directly from the request
// body (the pre-Day-27 behavior) let any caller set ownerId to an arbitrary user id. The
// owner is always the authenticated caller, taken from the JWT — never from the request body.
public sealed record CollectionCreateRequest(string Name);
