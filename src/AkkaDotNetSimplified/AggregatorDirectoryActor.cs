using Akka.Actor;
using Akka.DependencyInjection;

namespace AkkaDotNetSimplified;

// for DI
public readonly struct AggregatorDirectory;

public sealed class AggregatorDirectoryActor : ReceiveActor
{
    private readonly Dictionary<Guid, IActorRef> _aggregators = new();

    public AggregatorDirectoryActor()
    {
        Receive<LookupAggregator>(OnLookupAggregator);
        Receive<ShutdownAggregator>(OnShutdownAggregator);
    }

    private void OnLookupAggregator(LookupAggregator lookup)
    {
        var result = new Dictionary<Guid, IActorRef>(lookup.GroupIds.Count);

        foreach (var groupId in lookup.GroupIds)
        {
            if (!_aggregators.TryGetValue(groupId, out var aggregator))
            {
                aggregator = Context.ActorOf(
                    DependencyResolver
                        .For(Context.System)
                        .Props<AggregatorActor>(groupId));
                
                _aggregators.Add(groupId, aggregator);
            }
            result.Add(groupId, aggregator);
        }
        
        Context.Sender.Tell(new LookupResult(result));
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