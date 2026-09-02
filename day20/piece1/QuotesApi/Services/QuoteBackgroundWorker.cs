namespace QuotesApi.Services;

public sealed class QuoteBackgroundWorker : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly ILogger<QuoteBackgroundWorker> _logger;

    public QuoteBackgroundWorker(
        IBackgroundJobQueue queue,
        ILogger<QuoteBackgroundWorker> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quote background worker started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var workItem = await _queue.DequeueAsync(stoppingToken);

                try
                {
                    await workItem(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error occurred while processing background job.");
                }
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Quote background worker is stopping.");
        }

        _logger.LogInformation(
            "Quote background worker stopped.");
    }
}