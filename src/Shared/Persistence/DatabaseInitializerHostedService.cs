using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Retry;

namespace Shared.Persistence;

public sealed class DatabaseInitializerHostedService(MongoGroupItemRepository groupItemRepository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                BackoffType = DelayBackoffType.Exponential,
                MaxRetryAttempts = int.MaxValue,
                Delay = TimeSpan.FromSeconds(5),
            })
            .Build();

        await pipeline.ExecuteAsync(
            async ct => await groupItemRepository.InitializeDatabaseAsync(ct),
            cancellationToken);
    }

    // no-op
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}