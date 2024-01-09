using Proto;
using Proto.Cluster;

namespace ProtoActorSimplifiedWithBatchingOnForwarder;

public sealed class ActorSystemHostedService(ActorSystem system) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) 
        => await system.Cluster().StartMemberAsync();

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await system.Cluster().ShutdownAsync();
        await system.ShutdownAsync();
    }
}