using Akka.Actor;
using Akka.DependencyInjection;

namespace AkkaDotNetSimplified;

// for DI
public readonly struct Distributor;

public sealed class DistributorActor : ReceiveActor
{
    private readonly Dictionary<Guid, IActorRef> _aggregators = new();

    public DistributorActor()
    {
        Receive<Item>(OnItem);
        Receive<Persist>(OnPersist);
        Receive<ShutdownAggregator>(OnShutdownAggregator);
    }

    private void OnItem(Item item)
    {
        if (!_aggregators.TryGetValue(item.GroupId, out var aggregator))
        {
            aggregator = Context.ActorOf(
                DependencyResolver
                    .For(Context.System)
                    .Props<AggregatorActor>(item.GroupId));
            _aggregators.Add(item.GroupId, aggregator);
        }

        aggregator.Forward(item);
    }

    private void OnPersist(Persist persist)
    {
        if (!_aggregators.TryGetValue(persist.GroupId, out var aggregator))
        {
            // no aggregator for this group, nothing in memory to persist
            Context.Sender.Tell(Ack.Instance);
        }

        aggregator.Forward(persist);
    }

    private void OnShutdownAggregator(ShutdownAggregator completed)
    {
        if (_aggregators.TryGetValue(completed.GroupId, out var aggregator))
        {
            Context.Stop(aggregator);
            _aggregators.Remove(completed.GroupId);
        }
    }
}