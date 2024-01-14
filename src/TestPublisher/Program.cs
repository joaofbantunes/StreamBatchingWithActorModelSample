using Bogus;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Shared.Messages;

var topicName = "sample-streaming-topic";
var bootstrapServers = "localhost:9092";
//var bootstrapServers = "localhost:9192"; // testing with local k8s cluster

using var producer = new ProducerBuilder<Guid, Item>(
        new ProducerConfig
        {
            BootstrapServers = bootstrapServers
        })
    .SetKeySerializer(GuidSerializer.Instance)
    .SetValueSerializer(JsonMessageSerializer<Item>.Instance)
    .Build();

await CreateTopicAsync(topicName, bootstrapServers);

var groupings = Enumerable
    .Range(0, 100)
    //.Range(0, 1)
    .Select(_ => (Id: Guid.NewGuid(), Size: Random.Shared.Next(300, 3000)));

var publishTasks = groupings
    .Select(group => PublishGroupAsync(producer, topicName, group.Id, group.Size));

await Task.WhenAll(publishTasks);

static async Task PublishGroupAsync(IProducer<Guid, Item> producer, string topicName, Guid groupId, int size)
{
    var faker = new Faker();
    var items = Enumerable
        .Range(0, size)
        .Select(_ => new Item(groupId, Guid.NewGuid(), faker.Hacker.Verb()));

    var chunks = items.Chunk(100);
    foreach (var chunk in chunks)
    {
        await Task.Yield();
        
        foreach (var item in chunk)
        {
            producer.Produce(
                topicName,
                new Message<Guid, Item>
                {
                    // to mess things up, but given we have a actor system cluster, a single actor will handle all messages with the group id
                    //Key = Guid.NewGuid(),
                    Key = groupId,
                    Value = item
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