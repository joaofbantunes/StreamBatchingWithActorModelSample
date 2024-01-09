using Confluent.Kafka;
using Proto;
using Proto.Cluster;
using ProtoActorSimplifiedWithBatchingOnForwarder.Messages;

namespace ProtoActorSimplifiedWithBatchingOnForwarder;

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
            GroupId = "proto-actor-simplified2",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };

        using var consumer = new ConsumerBuilder<Guid, Shared.Messages.Item>(config)
            .SetKeyDeserializer(Shared.Messages.GuidDeserializer.Instance)
            .SetValueDeserializer(Shared.Messages.JsonMessageDeserializer<Shared.Messages.Item>.Instance)
            .Build();

        consumer.Subscribe("sample-streaming-topic");

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
            
            logger.LogInformation("Polled {PolledItemCount} records from Kafka", items.Sum(i => i.Items.Count));
            var pendingItems = items
                .Select(item => system.Cluster().RequestAsync<Ack>(
                    item.Id,
                    "group-aggregator",
                    item,
                    CancellationTokens.WithTimeout(BatchHandlingTimeout)));
            await Task.WhenAll(pendingItems);

            consumer.Commit();
        }
    }

    private IReadOnlyCollection<Batch> GetBatchFromKafka(IConsumer<Guid, Shared.Messages.Item> consumer)
    {
        var pollingStarted = timeProvider.GetTimestamp();
        try
        {
            var polled = new List<Shared.Messages.Item>(MaxPollBatchSize);
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
                .GroupBy(i => i.GroupingId)
                .Select(g =>
                {
                    var batch = new Batch
                    {
                        Id = g.Key.ToString()
                    };
                    batch.Items.AddRange(g.Select(Map));
                    return batch;
                }).ToArray();
        }
        finally
        {
            logger.LogDebug(
                "Spent {TimeSpent}s polling Kafka",
                timeProvider.GetElapsedTime(pollingStarted).TotalSeconds);
        }

        static BatchItem Map(Shared.Messages.Item item)
            => new()
            {
                Id = item.Id.ToString(),
                Stuff = item.Stuff
            };
    }
}