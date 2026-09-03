using Microsoft.AspNetCore.Mvc;
using QuotesApi.Services;

namespace QuotesApi.Controllers;

[ApiController]
[Route("api/background-jobs")]
public sealed class BackgroundJobsController : ControllerBase
{
    private readonly IBackgroundJobQueue _queue;
    private readonly ILogger<BackgroundJobsController> _logger;

    public BackgroundJobsController(
        IBackgroundJobQueue queue,
        ILogger<BackgroundJobsController> logger)
    {
        _queue = queue;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> QueueJob(
        CancellationToken cancellationToken)
    {
        await _queue.QueueAsync(async stoppingToken =>
        {
            _logger.LogInformation(
                "Background job started.");

            await Task.Delay(
                TimeSpan.FromSeconds(5),
                stoppingToken);

            _logger.LogInformation(
                "Background job completed.");
        });

        return Accepted(new
        {
            message = "Background job queued."
        });
    }
}