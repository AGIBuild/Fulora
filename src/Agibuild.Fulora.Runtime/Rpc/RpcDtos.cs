using System.Text.Json;
using System.Text.Json.Serialization;

namespace Agibuild.Fulora.Rpc;

// JSON-RPC 2.0 envelope DTOs. Promoted out of WebViewRpcService so individual
// pipeline stages (handler registry, dispatcher, serializer) can reference
// them without taking a hard dependency on the coordinator.

internal sealed class RpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Params { get; set; }
}

internal sealed class RpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Result { get; set; }
}

internal sealed class RpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RpcErrorData? Data { get; set; }
}

internal sealed class RpcErrorData
{
    [JsonPropertyName("diagnosticCode")]
    public int DiagnosticCode { get; set; }

    [JsonPropertyName("hint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hint { get; set; }
}

internal sealed class RpcErrorResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("error")]
    public RpcError? Error { get; set; }
}

internal sealed class EnumeratorNextResult
{
    [JsonPropertyName("values")]
    public object?[] Values { get; set; } = [];

    [JsonPropertyName("finished")]
    public bool Finished { get; set; }
}

internal sealed class EnumeratorInitResult
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = "";

    [JsonPropertyName("values")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object?[]? Values { get; set; }
}

[JsonSerializable(typeof(RpcRequest))]
[JsonSerializable(typeof(RpcResponse))]
[JsonSerializable(typeof(RpcErrorResponse))]
[JsonSerializable(typeof(RpcError))]
[JsonSerializable(typeof(RpcErrorData))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(DateTimeOffset))]
internal partial class RpcJsonContext : JsonSerializerContext
{
}
