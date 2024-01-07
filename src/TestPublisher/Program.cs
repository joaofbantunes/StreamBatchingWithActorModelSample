using Bogus;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Shared.Messages;

var topicName = "sample-batch-topic";
var bootstrapServers = "localhost:9092";

using var producer = new ProducerBuilder<Guid, BatchItem>(
        new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        })
    .SetKeySerializer(GuidSerializer.Instance)
    .SetValueSerializer(JsonMessageSerializer<BatchItem>.Instance)
    .Build();

await CreateTopicAsync(topicName, bootstrapServers);

var batches = Enumerable
    .Range(0, 100)
    .Select(_ => (Id: Guid.NewGuid(), Size: Random.Shared.Next(300, 3000)));

var publishTasks = batches
    .Select(batch => PublishBatchAsync(producer, batch.Id, batch.Size));

await Task.WhenAll(publishTasks);

static async Task PublishBatchAsync(IProducer<Guid, BatchItem> producer, Guid batchId, int batchSize)
{
    await Task.Yield();
    
    var faker = new Faker();
    var batchItems = Enumerable
        .Range(0, batchSize)
        .Select(_ => new BatchItem(new (batchId, batchSize), Guid.NewGuid(), faker.Hacker.Verb()));

    var chunks = batchItems.Chunk(100);
    foreach (var chunk in chunks)
    {
        foreach (var batchItem in chunk)
        {
            producer.Produce(
                "sample-batch-topic",
                new Message<Guid, BatchItem>
                {
                    Key = batchItem.BatchInfo.Id,
                    Value = batchItem
                });
        }

        producer.Flush();
    }
}

static async Task CreateTopicAsync(string topicName, string bootstrapServers)
{
    try
    {
        using var adminClient = new AdminClientBuilder(
                new AdminClientConfig
                {
                    BootstrapServers = bootstrapServers
                })
            .Build();

        await adminClient.CreateTopicsAsync(new TopicSpecification[]
        {
            new() { Name = topicName, ReplicationFactor = 1, NumPartitions = 10 }
        });
    }
    catch (CreateTopicsException)
    {
        // already exists, let's go
    }
}