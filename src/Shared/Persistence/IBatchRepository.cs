namespace Shared.Persistence;

public interface IBatchRepository
{
    Task<(Batch Batch, IReadOnlySet<Guid> BatchItems)?> LoadProgressAsync(Guid batchId, CancellationToken ct);
    
    Task SaveProgressAsync(Batch batch, IEnumerable<BatchItem> batchItems, CancellationToken ct);
    
    
}