namespace AkkaDotNetSimplified;

public sealed record Item(Guid GroupId, Guid Id, string Stuff);

public sealed record Persist(Guid GroupId);

public sealed record Ack
{
    public static Ack Instance { get; } = new();
}

public sealed record ShutdownAggregator(Guid GroupId);