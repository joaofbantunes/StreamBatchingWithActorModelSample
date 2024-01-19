using Confluent.Kafka;
using Proto;
using Proto.Cluster;
using ProtoActorWithBatchingOnForwarder.Messages;

namespace ProtoActorWithBatchingOnForwarder;

public sealed class KafkaConsumerHostedService(
    ActorSystem system,
    TimeProvider timeProvider,
    IConfiguration configuration,
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
            BootstrapServers = configuration.GetSection("Kafka")["BootstrapServers"],
            GroupId = "proto-actor-2",
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
            var chunks = GetBatchFromKafka(consumer);

            if (chunks.Count == 0)
            {
                logger.LogDebug(
                    "No records polled from Kafka, sleeping for {TimeToSleepWhenNoRecords}",
                    TimeToSleepWhenNoRecords);
                await Task.Delay(TimeToSleepWhenNoRecords, stoppingToken);
                continue;
            }
            
            logger.LogInformation("Polled {PolledItemCount} records from Kafka", chunks.Sum(c => c.Items.Count));
            
            var pendingChunks = chunks
                .Select(chunk => system.Cluster().RequestAsync<Ack>(
                    chunk.GroupId,
                    "group-aggregator",
                    chunk,
                    CancellationTokens.WithTimeout(BatchHandlingTimeout)));
            
            await Task.WhenAll(pendingChunks);

            consumer.Commit();
        }
    }

    private IReadOnlyCollection<GroupChunk> GetBatchFromKafka(IConsumer<Guid, Shared.Messages.Item> consumer)
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
                    var chunk = new GroupChunk
                    {
                        GroupId = g.Key.ToString()
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

        static GroupChunk.Types.Item Map(Shared.Messages.Item item)
            => new()
            {
                Id = item.Id.ToString(),
                Stuff = item.Stuff
            };
    }
}