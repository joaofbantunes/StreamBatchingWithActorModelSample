using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Shared.Persistence;

public sealed class MongoBatchRepository : IBatchRepository
{
    private readonly MongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<Batch> _batchCollection;
    private readonly IMongoCollection<BatchItem> _batchItemCollection;

    public MongoBatchRepository(IOptions<Settings> settings)
    {
        _client = new MongoClient(settings.Value.ConnectionString);
        _database = _client.GetDatabase(settings.Value.DatabaseName);
        _batchCollection = _database.GetCollection<Batch>("batches");
        _batchItemCollection = _database.GetCollection<BatchItem>("batch_items");
    }

    public async Task<(Batch Batch, IReadOnlySet<Guid> BatchItems)?> LoadProgressAsync(
        Guid batchId,
        CancellationToken ct)
    {
        var batch = await _batchCollection.Find(b => b.Id == batchId).SingleOrDefaultAsync(ct);
        if (batch == null) return null;
        var batchItems = await _batchItemCollection.Find(i => i.BatchId == batchId).Project(i => i.Id).ToListAsync(ct);
        return (batch, batchItems.ToHashSet());
    }

    public async Task SaveProgressAsync(Batch batch, IEnumerable<BatchItem> batchItems, CancellationToken ct)
    {
        await _batchCollection.ReplaceOneAsync(
            b => b.Id == batch.Id,
            batch,
            new ReplaceOptions { IsUpsert = true },
            ct);

        await _batchItemCollection.InsertManyAsync(
            batchItems,
            new InsertManyOptions { IsOrdered = false },
            ct);
    }

    internal async Task InitializeDatabaseAsync(CancellationToken ct)
    {
        await _batchItemCollection.Indexes.CreateOneAsync(
            new CreateIndexModel<BatchItem>(
                Builders<BatchItem>.IndexKeys.Ascending(i => i.BatchId),
                new CreateIndexOptions { Unique = false }),
            cancellationToken: ct);
    }

    public sealed record Settings
    {
        public required string ConnectionString { get; init; }
        public required string DatabaseName { get; init; }
    }
}