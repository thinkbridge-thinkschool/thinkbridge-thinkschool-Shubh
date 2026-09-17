using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace QuotesApi.Tests.Integration.Infrastructure;

// One SQL Server container and one Redis container for the whole test run, shared by every
// test class in the "Integration" collection below. Each test still gets its own isolated
// database (see QuotesApiFactory) and its own DI container/singletons (a fresh
// WebApplicationFactory per test), so sharing just the two containers only saves the
// (expensive) container startup cost, not test isolation.
public sealed class IntegrationTestContainers : IAsyncLifetime
{
    public MsSqlContainer Sql { get; } =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();

    public RedisContainer Redis { get; } =
        new RedisBuilder("redis:7-alpine")
            .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Sql.StartAsync(), Redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Sql.DisposeAsync();
        await Redis.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestContainers>
{
    public const string Name = "Integration";
}
