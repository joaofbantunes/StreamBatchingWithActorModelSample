using Confluent.Kafka;

namespace MsOrleans;

public sealed class KafkaConsumerHostedService(
    IGrainFactory grainFactory,
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
            GroupId = "orleans",
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
                .Select(chunk => grainFactory
                    .GetGrain<IAggregatorGrain>(chunk.GroupId)
                    .HandleGroupChunkAsync(chunk));

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
                .Select(g => new GroupChunk(g.Key.ToString(), g.Select(Map).ToArray()))
                .ToArray();
        }
        finally
        {
            logger.LogDebug(
                "Spent {TimeSpent}s polling Kafka",
                timeProvider.GetElapsedTime(pollingStarted).TotalSeconds);
        }

        static GroupChunkItem Map(Shared.Messages.Item item)
            => new(item.Id.ToString(), item.Stuff);
    }
}