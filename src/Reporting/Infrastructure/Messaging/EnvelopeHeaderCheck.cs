using System.Text;
using System.Text.Json;
using Chubb.Contracts;
using Chubb.Reporting.Domain;
using Confluent.Kafka;

namespace Chubb.Reporting.Infrastructure.Messaging;

// ADR-004: the envelope is the source of truth; a header that disagrees with it makes the message unusable.
public static class EnvelopeHeaderCheck
{
    public static void EnsureMatches(Headers headers, string message)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(message);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return;
        }

        EnsureEqual(headers, KafkaHeaderNames.EventId, ReadText(root, "eventId"));
        EnsureEqual(headers, KafkaHeaderNames.EventType, ReadText(root, "type"));
        EnsureEqual(headers, KafkaHeaderNames.EventVersion, ReadText(root, "version"));
    }

    private static void EnsureEqual(Headers headers, string name, string? envelopeValue)
    {
        if (!headers.TryGetLastBytes(name, out var bytes))
        {
            return;
        }

        if (!string.Equals(Encoding.UTF8.GetString(bytes), envelopeValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidEventException($"Header {name} does not match the envelope.");
        }
    }

    private static string? ReadText(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : value.ToString();
    }
}
