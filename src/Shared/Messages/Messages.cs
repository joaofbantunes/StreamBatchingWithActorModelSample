namespace Shared.Messages;

public sealed record BatchInfo(Guid Id, int Size);

public sealed record BatchItem(BatchInfo BatchInfo, Guid Id, string Stuff);