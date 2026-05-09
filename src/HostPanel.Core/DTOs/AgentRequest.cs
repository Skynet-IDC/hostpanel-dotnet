using System.Text.Json;
using System.Text.Json.Serialization;

namespace HostPanel.Core.DTOs;

public class AgentRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public Dictionary<string, object?> Params { get; set; } = [];

    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleIdConverter))]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Accepts JSON-RPC id as string, number, or null per the spec.
/// </summary>
public class FlexibleIdConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? "",
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
            JsonTokenType.Null => "",
            _ => ""
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
