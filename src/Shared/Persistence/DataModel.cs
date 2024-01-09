namespace Shared.Persistence;

public sealed record Group(Guid Id);

public sealed record GroupItem(Guid GroupId, Guid Id, string Stuff);