using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Shared.Persistence;

public sealed class MongoGroupItemRepository : IGroupItemRepository
{
    private readonly IMongoCollection<Group> _groupCollection;
    private readonly IMongoCollection<GroupItem> _groupItemCollection;

    public MongoGroupItemRepository(IOptions<Settings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        var database = client.GetDatabase(settings.Value.DatabaseName);
        _groupCollection = database.GetCollection<Group>("groups");
        _groupItemCollection = database.GetCollection<GroupItem>("group_items");
    }

    public async Task<(Group Group, IReadOnlySet<Guid> Items)?> LoadProgressAsync(
        Guid groupId,
        CancellationToken ct)
    {
        var group = await _groupCollection.Find(b => b.Id == groupId).SingleOrDefaultAsync(ct);
        if (group == null) return null;
        var items = await _groupItemCollection.Find(i => i.GroupId == groupId).Project(i => i.Id).ToListAsync(ct);
        return (group, items.ToHashSet());
    }

    public async Task SaveProgressAsync(Group group, IEnumerable<GroupItem> groupItems, CancellationToken ct)
    {
        await _groupCollection.FindOneAndReplaceAsync<Group>(
            new FilterDefinitionBuilder<Group>().Eq(g => g.Id, group.Id),
            group,
            new FindOneAndReplaceOptions<Group> { IsUpsert = true },
            ct);

        await _groupItemCollection.InsertManyAsync(
            groupItems,
            new InsertManyOptions { IsOrdered = false },
            ct);
    }

    internal async Task InitializeDatabaseAsync(CancellationToken ct)
    {
        await _groupItemCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<GroupItem>(
                Builders<GroupItem>.IndexKeys.Ascending(i => i.GroupId),
                new CreateIndexOptions { Unique = false }),
            cancellationToken: ct);
    }

    public sealed record Settings
    {
        public required string ConnectionString { get; init; }
        public required string DatabaseName { get; init; }
    }
}