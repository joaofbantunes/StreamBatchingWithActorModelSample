using Confluent.Kafka;
using Proto;
using Proto.Cluster;
using ProtoActor.Messages;

namespace ProtoActor;

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
            GroupId = "proto-actor",
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
                logger.LogDebug(
                    "No records polled from Kafka, sleeping for {TimeToSleepWhenNoRecords}",
                    TimeToSleepWhenNoRecords);
                await Task.Delay(TimeToSleepWhenNoRecords, stoppingToken);
                continue;
            }

            logger.LogInformation("Polled {BatchSize} records from Kafka", items.Count);
            var pendingItems = items
                .Select(item => system.Cluster().RequestAsync<Ack>(
                    item.GroupId,
                    "group-aggregator",
                    item,
                    CancellationTokens.WithTimeout(BatchHandlingTimeout)));
            await Task.WhenAll(pendingItems);

            var groupIds = items.Select(static item => item.GroupId).Distinct().ToArray();
            logger.LogInformation("Persisting {Groups} groups", groupIds.Length);
            var persistMessage = new Persist();
            var pendingPersists = groupIds
                .Select(groupId => system.Cluster().RequestAsync<Ack>(
                    groupId,
                    "group-aggregator",
                    persistMessage,
                    CancellationTokens.WithTimeout(BatchHandlingTimeout)));
            await Task.WhenAll(pendingPersists);

            consumer.Commit();
        }
    }

    private IReadOnlyCollection<Item> GetBatchFromKafka(IConsumer<Guid, Shared.Messages.Item> consumer)
    {
        var polled = new List<Item>(MaxPollBatchSize);
        var remainingTimeout = KafkaPollTimeout;

        while (polled.Count < MaxPollBatchSize && remainingTimeout > TimeSpan.Zero)
        {
            var startTime = timeProvider.GetTimestamp();
            var message = consumer.Consume(remainingTimeout);
            if (message != null)
            {
                polled.Add(Map(message.Message.Value));
            }

            remainingTimeout = remainingTimeout.Subtract(timeProvider.GetElapsedTime(startTime));
        }

        return polled;
    }

    private static Item Map(Shared.Messages.Item item)
        => new()
        {
            GroupId = item.GroupingId.ToString(),
            Id = item.Id.ToString(),
            Stuff = item.Stuff
        };
}