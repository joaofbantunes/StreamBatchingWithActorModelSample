namespace Shared.Persistence;

public interface IGroupItemRepository
{
    Task<(Group Group, IReadOnlySet<Guid> Items)?> LoadProgressAsync(Guid groupId, CancellationToken ct);
    
    Task SaveProgressAsync(Group group, IEnumerable<GroupItem> groupItems, CancellationToken ct);
    
    
}