using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Agibuild.Fulora.Rpc;

/// <summary>
/// Stateless serializer for JSON-RPC success and error responses. The
/// dispatcher hands a CLR result/diagnostic and the serializer takes care of:
/// <list type="bullet">
///   <item>Native AOT–safe handling of JSON primitives, common BCL types, and
///         <see cref="JsonElement"/>/<see cref="JsonDocument"/> pass-through.</item>
///   <item>Wrapping the result/error into the JSON-RPC 2.0 envelope using the
///         source-generated <see cref="RpcJsonContext"/>.</item>
///   <item>Optionally exposing diagnostic hints (DevTools mode) inside
///         <see cref="RpcErrorData"/>.</item>
/// </list>
/// Owning a single serializer keeps the AOT-safety reasoning in one place and
/// stops it from re-leaking into the dispatch pipeline.
/// </summary>
internal sealed class RpcResultSerializer
{
    /// <summary>
    /// Shared JSON options for bridge payload serialization: camelCase naming
    /// + case-insensitive deserialization. RPC envelope types use the
    /// source-generated <see cref="RpcJsonContext"/> and are unaffected.
    /// </summary>
    public static readonly JsonSerializerOptions BridgeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly bool _enableDevToolsDiagnostics;

    public RpcResultSerializer(bool enableDevToolsDiagnostics)
    {
        _enableDevToolsDiagnostics = enableDevToolsDiagnostics;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "RPC result serialization uses runtime types; the handler is responsible for type safety.")]
    public string BuildSuccessResponseJson(string? id, object? result)
    {
        var response = new RpcResponse
        {
            Id = id,
            Result = SerializeResultToElement(result)
        };
        return JsonSerializer.Serialize(response, RpcJsonContext.Default.RpcResponse);
    }

    public static string BuildErrorResponseJson(string? id, int code, string message)
    {
        var response = new RpcErrorResponse
        {
            Id = id,
            Error = new RpcError { Code = code, Message = message }
        };
        return JsonSerializer.Serialize(response, RpcJsonContext.Default.RpcErrorResponse);
    }

    public string BuildErrorResponseJson(string? id, BridgeErrorDiagnostic diagnostic)
    {
        var jsonRpcCode = BridgeErrorDiagnostic.ToJsonRpcCode(diagnostic.Code);
        var data = _enableDevToolsDiagnostics && diagnostic.Hint is not null
            ? new RpcErrorData { DiagnosticCode = (int)diagnostic.Code, Hint = diagnostic.Hint }
            : new RpcErrorData { DiagnosticCode = (int)diagnostic.Code };
        var response = new RpcErrorResponse
        {
            Id = id,
            Error = new RpcError { Code = jsonRpcCode, Message = diagnostic.Message, Data = data }
        };
        return JsonSerializer.Serialize(response, RpcJsonContext.Default.RpcErrorResponse);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Dynamic serialization is used only when dynamic code is supported; Native AOT falls back to explicit primitive handling.")]
    private static JsonElement? SerializeResultToElement(object? result)
    {
        if (result is null)
            return null;

        if (result is JsonElement element)
            return element.Clone();

        if (result is JsonDocument document)
            return document.RootElement.Clone();

        if (TrySerializeKnownResultToElement(result, out var known))
            return known;

        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            throw new NotSupportedException(
                $"Native AOT RPC result serialization requires primitive/JSON-compatible results or source-generated serializers. Unsupported result type: {result.GetType().FullName}.");
        }

        return JsonSerializer.SerializeToElement(result, BridgeJsonOptions);
    }

    private static bool TrySerializeKnownResultToElement(object result, out JsonElement element)
    {
        switch (result)
        {
            case string value:
                element = ParseJsonToken(JsonSerializer.Serialize(value, RpcJsonContext.Default.String));
                return true;
            case bool value:
                element = ParseJsonToken(value ? "true" : "false");
                return true;
            case byte value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case sbyte value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case short value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case ushort value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case int value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case uint value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case long value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case ulong value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case float value:
                element = ParseJsonToken(value.ToString("R", CultureInfo.InvariantCulture));
                return true;
            case double value:
                element = ParseJsonToken(value.ToString("R", CultureInfo.InvariantCulture));
                return true;
            case decimal value:
                element = ParseJsonToken(value.ToString(CultureInfo.InvariantCulture));
                return true;
            case char value:
                element = ParseJsonToken(JsonSerializer.Serialize(value.ToString(), RpcJsonContext.Default.String));
                return true;
            case Guid value:
                element = ParseJsonToken(JsonSerializer.Serialize(value.ToString(), RpcJsonContext.Default.String));
                return true;
            case DateTime value:
                element = ParseJsonToken(JsonSerializer.Serialize(value, RpcJsonContext.Default.DateTime));
                return true;
            case DateTimeOffset value:
                element = ParseJsonToken(JsonSerializer.Serialize(value, RpcJsonContext.Default.DateTimeOffset));
                return true;
            case TimeSpan value:
                element = ParseJsonToken(JsonSerializer.Serialize(value.ToString("c", CultureInfo.InvariantCulture), RpcJsonContext.Default.String));
                return true;
            case Uri value:
                element = ParseJsonToken(JsonSerializer.Serialize(value.ToString(), RpcJsonContext.Default.String));
                return true;
            default:
                if (result.GetType().IsEnum)
                {
                    element = ParseJsonToken(Convert.ToUInt64(result, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
                    return true;
                }

                element = default;
                return false;
        }
    }

    private static JsonElement ParseJsonToken(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
