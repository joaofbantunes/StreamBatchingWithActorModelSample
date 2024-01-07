using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace Shared.Messages;

public sealed class JsonMessageSerializer<T> : ISerializer<T> where T : class
{
    private JsonMessageSerializer()
    {
    }

    public static JsonMessageSerializer<T> Instance { get; } = new();
        
    public byte[] Serialize(T data, SerializationContext context)
        => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
}

public sealed class JsonMessageDeserializer<T> : IDeserializer<T> where T : class
{
    private JsonMessageDeserializer()
    {
    }

    public static JsonMessageDeserializer<T> Instance { get; } = new();
        
    public T Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext context)
        => JsonSerializer.Deserialize<T>(data)!;
}