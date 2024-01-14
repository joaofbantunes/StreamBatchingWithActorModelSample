using Akka.Actor;

namespace AkkaDotNetSimplified;

public sealed record LookupAggregator(IReadOnlyCollection<Guid> GroupIds);

public sealed record LookupResult(IReadOnlyDictionary<Guid, IActorRef> Aggregators);

public sealed record Item(Guid GroupId, Guid Id, string Stuff);

public sealed record Persist(Guid GroupId);

public sealed record Ack
{
    public static Ack Instance { get; } = new();
}

public sealed record ShutdownAggregator(Guid GroupId);