using Confluent.Kafka;

namespace Shared.Messages;

public sealed class GuidSerializer : ISerializer<Guid>
{
    private GuidSerializer()
    {
    }

    public static GuidSerializer Instance { get; } = new();

    public byte[] Serialize(Guid data, SerializationContext context) => data.ToByteArray();
}

public sealed class GuidDeserializer : IDeserializer<Guid>
{
    private GuidDeserializer()
    {
    }

    public static GuidDeserializer Instance { get; } = new();

    public Guid Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context) => new(data);
}