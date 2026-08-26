using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace PollyResilience.Tests;

public class UnitTest1
{
    [Fact]
    public async Task HttpClient_Retries_OnTransientFailure()
    {
        var services = new ServiceCollection();

        var attempts = 0;

        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() =>
                new FailingHandler(() =>
                {
                    attempts++;
                }))
            .AddResilienceHandler("default", builder =>
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 3,
                    BackoffType = DelayBackoffType.Constant,
                    Delay = TimeSpan.FromMilliseconds(10),
                    UseJitter = false
                });
            });

        var provider = services.BuildServiceProvider();

        var client = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("test");

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://test/failure"));

        Assert.Equal(4, attempts);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        private readonly Action _onRequest;

        public FailingHandler(Action onRequest)
        {
            _onRequest = onRequest;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _onRequest();

            throw new HttpRequestException(
                "Simulated transient failure");
        }
    }
}