namespace Shared.Persistence;

public sealed record Batch(Guid Id, int Size);

public sealed record BatchItem(Guid Id, Guid BatchId, string Stuff);