using Shared.Persistence;

namespace MsOrleans;

[Immutable]
[GenerateSerializer]
public sealed record GroupChunkItem(string Id, string Stuff);

[Immutable]
[GenerateSerializer]
public sealed record GroupChunk(string GroupId, IReadOnlyList<GroupChunkItem> Items);

public interface IAggregatorGrain : IGrainWithStringKey
{
    Task HandleGroupChunkAsync(GroupChunk chunk);
}

public sealed class AggregatorGrain(
    IGroupItemRepository groupItemRepository,
    TimeProvider timeProvider,
    ILogger<AggregatorGrain> logger)
    : Grain, IAggregatorGrain, IDisposable
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromMinutes(1);

    private IGrainTimer? _idleCheckTimer;
    private Guid _groupId;
    private HashSet<string> _handledItems = [];
    private long _lastReceivedTimestamp;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _groupId = Guid.Parse(this.GetPrimaryKeyString());
        var progress = await groupItemRepository.LoadProgressAsync(_groupId, ct);
        _handledItems = progress.HasValue
            ? progress.Value.Items.Select(i => i.ToString()).ToHashSet()
            : [];
        
        _idleCheckTimer = this.RegisterGrainTimer(OnIdleCheckAsync, ReceiveTimeout, ReceiveTimeout);
        await base.OnActivateAsync(ct);
    }

    // TODO: can we get a cancellation token here?
    public async Task HandleGroupChunkAsync(GroupChunk chunk)
    {
        var senderHost = RequestContext.Get("SenderHost") as string;
        logger.LogInformation("Received chunk in {GrainId} from server {SenderHost}", GrainContext.GrainId, senderHost);
        
        var items = chunk.Items
            .Where(i => _handledItems.Add(i.Id))
            .Select(i => new GroupItem(_groupId, Guid.Parse(i.Id), i.Stuff))
            .ToArray();

        if (items.Length > 0)
        {
            await groupItemRepository.SaveProgressAsync(new Group(_groupId), items, CancellationToken.None);
        }
        
        _lastReceivedTimestamp = timeProvider.GetTimestamp();
    }

    private Task OnIdleCheckAsync(CancellationToken ct)
    {
        if (timeProvider.GetElapsedTime(_lastReceivedTimestamp) >= ReceiveTimeout)
        {
            logger.LogInformation("No data received for {ReceiveTimeout} seconds, deactivating grain.", ReceiveTimeout.TotalSeconds);
            DeactivateOnIdle();
        }
        
        return Task.CompletedTask;
    }

    public void Dispose() => _idleCheckTimer?.Dispose();
}