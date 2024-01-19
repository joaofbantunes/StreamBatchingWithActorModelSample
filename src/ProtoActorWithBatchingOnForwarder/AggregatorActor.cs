using Proto;
using Proto.Cluster;
using ProtoActorWithBatchingOnForwarder.Messages;
using Shared.Persistence;

namespace ProtoActorWithBatchingOnForwarder;

public sealed class AggregatorActor(IGroupItemRepository groupItemRepository, ILogger<AggregatorActor> logger) : IActor
{
    private static readonly Ack Ack = new();
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    // initialized on startup
    private Guid _groupId;
    private HashSet<string> _handledItems = new();

    public Task ReceiveAsync(IContext context)
        => context.Message switch
        {
            Started _ => OnStarted(context),
            GroupChunk chunk => OnChunk(context, chunk),
            ReceiveTimeout _ => OnReceiveTimeout(context),
            Stopping _ => OnStopping(context),
            _ => Task.CompletedTask
        };

    private async Task OnStarted(IContext context)
    {
        _groupId = Guid.Parse(context.ClusterIdentity()!.Identity);
        var progress = await groupItemRepository.LoadProgressAsync(_groupId, context.CancellationToken);
        if (progress.HasValue)
        {
            _handledItems = progress.Value.Items.Select(i => i.ToString()).ToHashSet();
        }
        else
        {
            _handledItems = new HashSet<string>();
        }

        context.SetReceiveTimeout(ReceiveTimeout);
    }

    private async Task OnChunk(IContext context, GroupChunk chunk)
    {
        // this log is to show that, when we don't have local affinity enabled, and the Kafka keys are not well defined,
        // the messages are still sent to the same actor instance, because the group id is the same
        logger.LogInformation("Received chunk from {Sender}", context.Sender?.ToDiagnosticString());
        
        var items = chunk.Items
            .Where(i => _handledItems.Add(i.Id))
            .Select(i => new GroupItem(_groupId, Guid.Parse(i.Id), i.Stuff))
            .ToArray();

        if (items.Length > 0)
        {
            await groupItemRepository.SaveProgressAsync(
                new Group(_groupId),
                items,
                context.CancellationToken);
        }

        context.Respond(Ack);
    }

    private Task OnReceiveTimeout(IContext context)
    {
        context.Poison(context.Self);
        return Task.CompletedTask;
    }

    private Task OnStopping(IContext context)
    {
        logger.LogInformation("Stopping actor of group {GroupId}", _groupId);
        return Task.CompletedTask;
    }
}