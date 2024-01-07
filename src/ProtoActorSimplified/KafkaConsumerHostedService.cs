using Confluent.Kafka;
using Proto;
using Proto.Cluster;
using ProtoActorSimplified.Messages;

namespace ProtoActorSimplified;

public sealed class KafkaConsumerHostedService(
    ActorSystem system,
    TimeProvider timeProvider,
    ILogger<KafkaConsumerHostedService> logger)
    : BackgroundService
{
    private static readonly TimeSpan BatchHandlingTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KafkaPollTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TimeToSleepWhenNoRecords = TimeSpan.FromSeconds(5);

    private const int MaxPollBatchSize = 1000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // don't block startup

        var config = new ConsumerConfig
        {
            BootstrapServers = "localhost:9092",
            GroupId = "proto-actor-simplified",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<Guid, Shared.Messages.BatchItem>(config)
            .SetKeyDeserializer(Shared.Messages.GuidDeserializer.Instance)
            .SetValueDeserializer(Shared.Messages.JsonMessageDeserializer<Shared.Messages.BatchItem>.Instance)
            .Build();

        consumer.Subscribe("sample-batch-topic");

        // TODO: handle errors
        while (!stoppingToken.IsCancellationRequested)
        {
            var items = GetBatchFromKafka(consumer);

            if (items.Count == 0)
            {
                logger.LogDebug("No records polled from Kafka, sleeping for {TimeToSleepWhenNoRecords}",
                    TimeToSleepWhenNoRecords);
                await Task.Delay(TimeToSleepWhenNoRecords, stoppingToken);
                continue;
            }

            //logger.LogInformation("Polled {BatchSize} records from Kafka", items.Count);
            logger.LogInformation("Polled {BatchSize} records from Kafka", items.Sum(i => i.Items.Count));
            var pendingItems = items
                .Select(item => system.Cluster().RequestAsync<Ack>(
                    item.BatchInfo.Id,
                    "batch-aggregator",
                    item,
                    CancellationTokens.WithTimeout(BatchHandlingTimeout)));
            await Task.WhenAll(pendingItems);

            var batchIds = items.Select(static item => item.BatchInfo.Id).Distinct().ToArray();
            logger.LogInformation("Persisting {Batches} batches", batchIds.Length);
            var persistMessage = new Persist();
            var pendingPersists = batchIds
                .Select(batchId => system.Cluster().RequestAsync<Ack>(
                    batchId,
                    "batch-aggregator",
                    persistMessage,
                    CancellationTokens.WithTimeout(BatchHandlingTimeout)));
            await Task.WhenAll(pendingPersists);

            consumer.Commit();
        }
    }

    private IReadOnlyCollection<BatchChunk> GetBatchFromKafka(IConsumer<Guid, Shared.Messages.BatchItem> consumer)
    {
        var pollingStarted = timeProvider.GetTimestamp();
        try
        {
            var polled = new List<Shared.Messages.BatchItem>(MaxPollBatchSize);
            var remainingTimeout = KafkaPollTimeout;

            while (polled.Count < MaxPollBatchSize && remainingTimeout > TimeSpan.Zero)
            {
                var startTime = timeProvider.GetTimestamp();
                var message = consumer.Consume(remainingTimeout);
                if (message != null)
                {
                    polled.Add(message.Message.Value);
                }

                remainingTimeout = remainingTimeout.Subtract(timeProvider.GetElapsedTime(startTime));
            }

            return polled
                .GroupBy(i => i.BatchInfo.Id)
                .Select(g =>
                {
                    var chunk = new BatchChunk
                    {
                        BatchInfo = new()
                        {
                            Id = g.Key.ToString(),
                            Size = g.First().BatchInfo.Size
                        },
                    };
                    chunk.Items.AddRange(g.Select(Map));
                    return chunk;
                }).ToArray();
        }
        finally
        {
            logger.LogDebug(
                "Spent {TimeSpent}s polling Kafka",
                timeProvider.GetElapsedTime(pollingStarted).TotalSeconds);
        }

        static BatchItem Map(Shared.Messages.BatchItem item)
            => new()
            {
                Id = item.Id.ToString(),
                Stuff = item.Stuff
            };
    }

    // private IReadOnlyCollection<BatchItem> GetBatchFromKafka(IConsumer<Guid, Shared.Messages.BatchItem> consumer)
    // {
    //     var polled = new List<BatchItem>(MaxPollBatchSize);
    //     var remainingTimeout = KafkaPollTimeout;
    //
    //     while (polled.Count < MaxPollBatchSize && remainingTimeout > TimeSpan.Zero)
    //     {
    //         var startTime = timeProvider.GetTimestamp();
    //         var message = consumer.Consume(remainingTimeout);
    //         if (message != null)
    //         {
    //             polled.Add(Map(message.Message.Value));
    //         }
    //
    //         remainingTimeout = remainingTimeout.Subtract(timeProvider.GetElapsedTime(startTime));
    //     }
    //
    //     return polled;
    //     
    //     private static BatchItem Map(Shared.Messages.BatchItem item)
    //         => new()
    //         {
    //             BatchInfo = new()
    //             {
    //                 Id = item.BatchInfo.Id.ToString(),
    //                 Size = item.BatchInfo.Size
    //             },
    //             Id = item.Id.ToString(),
    //             Stuff = item.Stuff
    //         };
    // }
}