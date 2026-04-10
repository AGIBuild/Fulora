using System.Diagnostics.CodeAnalysis;

namespace Agibuild.Fulora;

internal interface IRuntimeBridgeStrategy
{
    bool TryExpose<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        T implementation,
        BridgeOptions? options,
        out ExposedService exposedService) where T : class;

    bool TryCreateProxy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        out T? proxy) where T : class;
}
