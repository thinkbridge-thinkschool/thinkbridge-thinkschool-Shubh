namespace QuotesApi.Services;

public interface IBackgroundJobQueue
{
    ValueTask QueueAsync(
        Func<CancellationToken, ValueTask> workItem);

    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
        CancellationToken cancellationToken);
}