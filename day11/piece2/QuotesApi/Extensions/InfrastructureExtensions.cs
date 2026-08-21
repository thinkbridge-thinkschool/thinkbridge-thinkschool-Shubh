using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Repositories;

namespace QuotesApi.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        services.AddDbContext<QuotesDbContext>(options =>
            options.UseSqlite("Data Source=quotes.db"));

        services.AddScoped<IQuoteRepository, QuoteRepository>();

        return services;
    }
}