using Proto;
using Proto.Cluster;
using ProtoActorSimplifiedWithBatchingOnForwarder.Messages;
using Shared.Persistence;

namespace ProtoActorSimplifiedWithBatchingOnForwarder;

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
            Batch batch => OnBatch(context, batch),
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

    private async Task OnBatch(IContext context, Batch batch)
    {
        // this log is to show that, when we don't have local affinity enabled, and the Kafka keys are not well defined,
        // the messages are still sent to the same actor instance, because the group id is the same
        logger.LogInformation("Received batch from {Sender}", context.Sender?.ToDiagnosticString());
        
        var items = batch.Items
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