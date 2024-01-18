using Proto;
using Proto.Cluster;

namespace ProtoActorWithBatchingOnForwarder;

public sealed class ActorSystemHostedService(ActorSystem system) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken) 
        => await system.Cluster().StartMemberAsync();

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var reason = "Service shutting down";
        await system.Cluster().ShutdownAsync(reason: reason);
        await system.ShutdownAsync(reason: reason);
    }
}