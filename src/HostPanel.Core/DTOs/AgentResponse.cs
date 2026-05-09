using System.Text.Json.Serialization;

namespace HostPanel.Core.DTOs;

public class AgentResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    [JsonPropertyName("result")]
    public AgentResult? Result { get; set; }

    [JsonPropertyName("error")]
    public AgentError? Error { get; set; }

    public bool IsSuccess => Error is null;
}

public class AgentResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, object?>? Data { get; set; }
}

public class AgentError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
