using Proto;
using Proto.Cluster;
using ProtoActor.Messages;
using Shared.Persistence;

namespace ProtoActor;

public sealed class AggregatorActor(IGroupItemRepository groupItemRepository, ILogger<AggregatorActor> logger) : IActor
{
    private static readonly Ack Ack = new();
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    // initialized on startup
    private Guid _groupId;
    private HashSet<string> _handledItems = new();
    private List<Item> _persistableItems = new();

    public Task ReceiveAsync(IContext context)
        => context.Message switch
        {
            Started _ => OnStarted(context),
            Item item => OnItem(context, item),
            Persist _ => OnPersist(context),
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

    private Task OnItem(IContext context, Item item)
    {
        if (_handledItems.Add(item.Id))
        {
            _persistableItems.Add(item);
        }
        
        context.Respond(Ack);

        return Task.CompletedTask;
    }
    
    private async Task OnPersist(IContext context)
    {
        if (_persistableItems.Count > 0)
        {
            await groupItemRepository.SaveProgressAsync(
                new Group(_groupId),
                _persistableItems.Select(
                    i => new GroupItem(_groupId, Guid.Parse(i.Id), i.Stuff)),
                context.CancellationToken);

            _persistableItems.Clear();
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