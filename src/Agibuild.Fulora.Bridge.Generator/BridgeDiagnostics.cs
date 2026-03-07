using Microsoft.CodeAnalysis;

namespace Agibuild.Fulora.Bridge.Generator;

/// <summary>
/// Diagnostic descriptors for V1 bridge scope boundary violations.
/// </summary>
internal static class BridgeDiagnostics
{
    private const string Category = "Agibuild.Fulora.Bridge";

    public static readonly DiagnosticDescriptor GenericMethodNotSupported = new(
        id: "AGBR001",
        title: "Generic methods are not supported in bridge interfaces",
        messageFormat: "Bridge method '{0}' has generic type parameters, which cannot be resolved by the source generator at compile time. Use concrete method signatures instead, or define a non-generic interface per concrete type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OverloadNotSupported = new(
        id: "AGBR002",
        title: "Method overloads with same parameter count are not supported in bridge interfaces",
        messageFormat: "Bridge interface '{0}' has overloaded method '{1}' with conflicting parameter counts that cannot be disambiguated. Ensure each overload has a distinct number of parameters.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor RefOutInNotSupported = new(
        id: "AGBR003",
        title: "ref/out/in parameters are not supported in bridge interfaces",
        messageFormat: "Bridge method '{0}' has {1} parameter '{2}', which is not supported",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CancellationTokenNotSupported = new(
        id: "AGBR004",
        title: "CancellationToken is not yet supported in bridge interfaces",
        messageFormat: "Bridge method '{0}' has CancellationToken parameter, which is not supported in V1. This will be supported in a future version.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AsyncEnumerableNotSupported = new(
        id: "AGBR005",
        title: "IAsyncEnumerable is not yet supported in bridge interfaces",
        messageFormat: "Bridge method '{0}' returns IAsyncEnumerable, which is not supported in V1. This will be supported in a future version.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenGenericInterfaceNotSupported = new(
        id: "AGBR006",
        title: "Open generic interfaces are not supported in bridge",
        messageFormat: "Bridge interface '{0}' has open generic type parameters. Use a concrete closed generic type instead (e.g., IRepository<User> instead of IRepository<T>).",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BridgeEventOnImportNotSupported = new(
        id: "AGBR007",
        title: "IBridgeEvent properties are not supported on [JsImport] interfaces",
        messageFormat: "Bridge interface '{0}' has IBridgeEvent property '{1}'. Event channels can only push from C# to JS and are only supported on [JsExport] interfaces.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AiToolWithoutJsExport = new(
        id: "AGBR009",
        title: "[AiTool] requires [JsExport]",
        messageFormat: "Interface '{0}' has [AiTool] but is missing [JsExport]. AI tool schema generation requires [JsExport] to expose the method via the bridge.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AiToolMissingXmlDoc = new(
        id: "AGBR010",
        title: "[AiTool] method missing XML documentation",
        messageFormat: "Method '{0}' on interface '{1}' has [AiTool] but no XML <summary>. AI providers use the description to understand tool functionality.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
