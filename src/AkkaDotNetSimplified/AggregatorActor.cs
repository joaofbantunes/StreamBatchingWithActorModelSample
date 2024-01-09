using Akka.Actor;
using Shared.Persistence;

namespace AkkaDotNetSimplified;

public sealed class AggregatorActor : ReceiveActor
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    private readonly IGroupItemRepository _groupItemRepository;
    private readonly Guid _groupId;

    // initialized on first item
    private bool _isInitialized = false;
    private HashSet<Guid> _handledItems = new();
    private List<Item> _persistableItems = new();

    public AggregatorActor(Guid groupId, IGroupItemRepository groupItemRepository)
    {
        _groupId = groupId;
        _groupItemRepository = groupItemRepository;

        ReceiveAsync<Item>(OnItem);
        ReceiveAsync<Persist>(OnPersist);
        Receive<ReceiveTimeout>(OnReceiveTimeout);
        SetReceiveTimeout(ReceiveTimeout);
    }

    private async Task OnItem(Item item)
    {
        await Initialize();

        if (_handledItems.Add(item.Id))
        {
            _persistableItems.Add(item);
        }

        Context.Sender.Tell(Ack.Instance);
    }

    private async Task Initialize()
    {
        if (!_isInitialized)
        {
            var progress = await _groupItemRepository.LoadProgressAsync(_groupId, CancellationToken.None);
            if (progress.HasValue)
            {
                _handledItems = progress.Value.Items.Select(i => i).ToHashSet();
            }

            _isInitialized = true;
        }
    }

    private async Task OnPersist(Persist _)
    {
        if (_persistableItems.Count > 0)
        {
            await _groupItemRepository.SaveProgressAsync(
                new Group(_groupId),
                _persistableItems.Select(
                    i => new Shared.Persistence.GroupItem(_groupId, i.Id, i.Stuff)),
                CancellationToken.None);

            _persistableItems.Clear();
        }

        Context.Sender.Tell(Ack.Instance);
    }

    private void OnReceiveTimeout(ReceiveTimeout _)
    {
        Context.Parent.Tell(new ShutdownAggregator(_groupId));
    }
}