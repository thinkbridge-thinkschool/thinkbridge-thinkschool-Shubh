using Microsoft.AspNetCore.Authorization;
namespace QuotesApi.Modules.Quotes.Api.Authorization;
public class OwnsQuoteRequirement : IAuthorizationRequirement
{
}