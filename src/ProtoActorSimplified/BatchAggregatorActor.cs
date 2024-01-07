using Proto;
using Proto.Cluster;
using ProtoActorSimplified.Messages;
using Shared.Persistence;
using BatchItem = ProtoActorSimplified.Messages.BatchItem;

namespace ProtoActorSimplified;

public sealed class BatchAggregatorActor(IBatchRepository batchRepository) : IActor
{
    private static readonly Ack Ack = new();
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    // initialized on startup
    private Guid _batchId;
    private int _batchSize;
    private HashSet<string> _handledBatchItems = new();
    private List<BatchItem> _persistableBatchItems = new();

    public Task ReceiveAsync(IContext context)
        => context.Message switch
        {
            Started _ => OnStarted(context),
            BatchChunk chunk => OnBatchChunk(context, chunk),
            //BatchItem item => OnBatchItem(context, item),
            Persist _ => OnPersist(context),
            ReceiveTimeout _ => OnReceiveTimeout(context),
            _ => Task.CompletedTask
        };

    private async Task OnStarted(IContext context)
    {
        _batchId = Guid.Parse(context.ClusterIdentity()!.Identity);
        var progress = await batchRepository.LoadProgressAsync(_batchId, context.CancellationToken);
        if (progress.HasValue)
        {
            _batchSize = progress.Value.Batch.Size;
            _handledBatchItems = progress.Value.BatchItems.Select(i => i.ToString()).ToHashSet();
        }
        else
        {
            _batchSize = -1; // unknown until the first batch item arrives
            _handledBatchItems = new HashSet<string>();
        }

        context.SetReceiveTimeout(ReceiveTimeout);
    }

    private Task OnBatchChunk(IContext context, BatchChunk chunk)
    {
        _batchSize = chunk.BatchInfo.Size;
    
        if (_batchSize == _handledBatchItems.Count)
        {
            context.Respond(Ack);
            return Task.CompletedTask;
        }
    
        foreach (var item in chunk.Items)
        {
            if (_handledBatchItems.Add(item.Id))
            {
                _persistableBatchItems.Add(item);
            }
        }
    
        context.Respond(Ack);
    
        return Task.CompletedTask;
    }

    // private Task OnBatchItem(IContext context, BatchItem item)
    // {
    //     _batchSize = item.BatchInfo.Size;
    //
    //     if (_batchSize == _handledBatchItems.Count)
    //     {
    //         context.Respond(Ack);
    //         return Task.CompletedTask;
    //     }
    //
    //     if (_handledBatchItems.Add(item.Id))
    //     {
    //         _persistableBatchItems.Add(item);
    //     }
    //
    //
    //     context.Respond(Ack);
    //
    //     return Task.CompletedTask;
    // }


    private async Task OnPersist(IContext context)
    {
        if (_persistableBatchItems.Count > 0)
        {
            await batchRepository.SaveProgressAsync(
                new Batch(_batchId, _batchSize),
                _persistableBatchItems.Select(
                    i => new Shared.Persistence.BatchItem(Guid.Parse(i.Id), _batchId, i.Stuff)),
                context.CancellationToken);

            _persistableBatchItems.Clear();
        }
        
        if (_batchSize > 0 && _batchSize == _handledBatchItems.Count)
        {
            // ReSharper disable once MethodHasAsyncOverload - don't use the async version, as it can't wait for itself
            context.Poison(context.Self);
        }
        
        context.Respond(Ack);
    }

    private Task OnReceiveTimeout(IContext context)
    {
        context.Poison(context.Self);
        return Task.CompletedTask;
    }
}