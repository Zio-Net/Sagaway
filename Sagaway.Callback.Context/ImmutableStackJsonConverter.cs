using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sagaway.Callback.Context;

/// <summary>
/// Custom JSON converter for ImmutableStack&lt;T&gt;.
/// </summary>
/// <typeparam name="T">The type of elements in the stack.</typeparam>
public class ImmutableStackJsonConverter<T> : JsonConverter<ImmutableStack<T>>
{
    /// <summary>
    /// Reads and converts JSON to an instance of ImmutableStack&lt;T&gt;.
    /// </summary>
    /// <param name="reader">The reader to read JSON from.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>An instance of ImmutableStack&lt;T&gt; deserialized from JSON.</returns>
    public override ImmutableStack<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException("Expected start of array.");
        }

        var list = new List<T>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            var entry = JsonSerializer.Deserialize<T>(ref reader, options);
            if (entry != null)
            {
                list.Add(entry);
            }
        }

        // Reverse the list to maintain the original stack order
        list.Reverse();
        return ImmutableStack.CreateRange(list);
    }

    /// <summary>
    /// Writes an instance of ImmutableStack&lt;T&gt; to JSON.
    /// </summary>
    /// <param name="writer">The writer to write JSON to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, ImmutableStack<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteEndArray();
    }
}
