using Microsoft.Extensions.Hosting;

namespace Shared.Persistence;

public sealed class DatabaseInitializerHostedService(MongoGroupItemRepository groupItemRepository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await groupItemRepository.InitializeDatabaseAsync(cancellationToken);
    }

    // no-op
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}