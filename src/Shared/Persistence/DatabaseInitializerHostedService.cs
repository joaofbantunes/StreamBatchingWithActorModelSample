using Microsoft.Extensions.Hosting;

namespace Shared.Persistence;

public sealed class DatabaseInitializerHostedService(MongoBatchRepository batchRepository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await batchRepository.InitializeDatabaseAsync(cancellationToken);
    }

    // no-op
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}