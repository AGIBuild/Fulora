using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Agibuild.Fulora.UnitTests;

public class AotCompatibilityTests
{
    [Fact]
    public void AotCompatibilityAttribute_can_be_constructed_and_read()
    {
        var attr = new AotCompatibilityAttribute(isAotCompatible: true);
        Assert.True(attr.IsAotCompatible);

        var attrFalse = new AotCompatibilityAttribute(isAotCompatible: false);
        Assert.False(attrFalse.IsAotCompatible);

        var attrDefault = new AotCompatibilityAttribute();
        Assert.True(attrDefault.IsAotCompatible);
    }

    [Fact]
    public void Core_assembly_has_AotCompatibilityAttribute()
    {
        var coreAssembly = typeof(ServiceWorkerOptions).Assembly;
        var attr = coreAssembly.GetCustomAttribute<AotCompatibilityAttribute>();
        Assert.NotNull(attr);
        Assert.True(attr.IsAotCompatible);
    }

    [Fact]
    public void JsonSerializerOptions_in_Runtime_use_source_generated_contexts()
    {
        var runtimeAssembly = typeof(ServiceWorkerRegistrar).Assembly;
        var rpcContextType = runtimeAssembly.GetType("Agibuild.Fulora.Rpc.RpcJsonContext", throwOnError: false);
        Assert.NotNull(rpcContextType);
        Assert.True(typeof(JsonSerializerContext).IsAssignableFrom(rpcContextType));

        var defaultProp = rpcContextType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(defaultProp);
    }

    [Fact]
    public void BridgeService_registration_uses_IBridgeServiceRegistration()
    {
        var runtimeAssembly = typeof(ServiceWorkerRegistrar).Assembly;
        var generatedPathType = runtimeAssembly.GetType("Agibuild.Fulora.RuntimeBridgeGeneratedPath", throwOnError: false);
        Assert.NotNull(generatedPathType);

        var findMethod = generatedPathType.GetMethod("FindRegistration",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(findMethod);

        var returnType = findMethod.ReturnType;
        Assert.True(returnType.IsGenericType);
        // FindGeneratedRegistration<T> returns IBridgeServiceRegistration<T>?
        var genericDef = returnType.GetGenericTypeDefinition();
        Assert.Equal(typeof(IBridgeServiceRegistration<>), genericDef);
    }

    [Fact]
    public void Core_assembly_has_no_reflection_based_serialization_breaking_AOT()
    {
        var coreSourceDir = GetCoreSourceDirectory();
        if (coreSourceDir is null)
            return; // Skip if source not available (e.g. in packaged test run)

        var problematicPatterns = new[] { "Type.GetType(", "Assembly.Load(", "Assembly.LoadFrom(" };
        var csFiles = Directory.GetFiles(coreSourceDir, "*.cs", SearchOption.AllDirectories);

        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var pattern in problematicPatterns)
            {
                if (content.Contains(pattern))
                    Assert.Fail($"Core assembly should not use {pattern} (AOT-incompatible). Found in {Path.GetFileName(file)}");
            }
        }
    }

    [Fact]
    public void Core_assembly_avoids_dynamic_loading_patterns()
    {
        var coreSourceDir = GetCoreSourceDirectory();
        if (coreSourceDir is null)
            return;

        var csFiles = Directory.GetFiles(coreSourceDir, "*.cs", SearchOption.AllDirectories);
        foreach (var file in csFiles)
        {
            var content = File.ReadAllText(file);
            if (content.Contains("Type.GetType(") || content.Contains("Assembly.Load(") || content.Contains("Assembly.LoadFrom("))
            {
                Assert.Fail($"Core should avoid dynamic loading. Found in {Path.GetFileName(file)}");
            }
        }
    }

    [Fact]
    public void Key_types_do_not_use_dynamic_features()
    {
        var bridgeRegAttr = typeof(BridgeRegistrationAttribute);
        var ctor = bridgeRegAttr.GetConstructor([typeof(Type), typeof(Type)]);
        Assert.NotNull(ctor);
        var regTypeParam = ctor.GetParameters()[1];
        var dam = regTypeParam.GetCustomAttribute<System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembersAttribute>();
        Assert.NotNull(dam);
    }

    [Fact]
    public void RuntimeBridgeService_reflection_fallback_is_guarded_for_AOT()
    {
        var runtimeSourceDir = GetRuntimeSourceDirectory();
        if (runtimeSourceDir is null)
            return;

        var source = File.ReadAllText(Path.Combine(runtimeSourceDir, "RuntimeBridgeService.cs"));
        var strategySource = File.ReadAllText(Path.Combine(runtimeSourceDir, "IRuntimeBridgeStrategy.cs"));
        var defaultsSource = File.ReadAllText(Path.Combine(runtimeSourceDir, "RuntimeBridgeStrategyDefaults.cs"));
        var fallbackSource = File.ReadAllText(Path.Combine(runtimeSourceDir, "RuntimeBridgeDynamicFallback.cs"));
        var generatedSource = File.ReadAllText(Path.Combine(runtimeSourceDir, "RuntimeBridgeGeneratedPath.cs"));
        Assert.Contains("IRuntimeBridgeStrategy", source);
        Assert.Contains("RuntimeBridgeStrategyDefaults", source);
        Assert.Contains("TryExpose", strategySource);
        Assert.Contains("TryCreateProxy", strategySource);
        Assert.Contains("RuntimeBridgeDynamicFallback", defaultsSource);
        Assert.Contains("RuntimeBridgeGeneratedPath", defaultsSource);
        Assert.DoesNotContain("RuntimeFeature.IsDynamicCodeSupported", source);
        Assert.DoesNotContain("private void ExposeViaReflection", source);
        Assert.DoesNotContain("private T CreateImportProxy", source);
        Assert.DoesNotContain("FindGeneratedRegistration", source);
        Assert.DoesNotContain("FindGeneratedProxy", source);
        Assert.Contains("RequiresDynamicCode", fallbackSource);
        Assert.Contains("RuntimeFeature.IsDynamicCodeSupported", fallbackSource);
        Assert.Contains("FindRegistration", generatedSource);
    }

    [Fact]
    public void WebViewRpcService_dynamic_result_serialization_is_guarded_for_AOT()
    {
        var runtimeSourceDir = GetRuntimeSourceDirectory();
        if (runtimeSourceDir is null)
            return;

        // Result serialization (and its AOT guard) was extracted into the
        // dedicated facet under Rpc/. The contract still belongs to the RPC
        // service surface, so this test continues to live here.
        var source = File.ReadAllText(Path.Combine(runtimeSourceDir, "Rpc", "RpcResultSerializer.cs"));
        Assert.Contains("RuntimeFeature.IsDynamicCodeSupported", source);
        Assert.Contains("SerializeResultToElement", source);
    }

    private static string? GetCoreSourceDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Agibuild.Fulora.Core"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Agibuild.Fulora.Core"),
        };

        foreach (var dir in candidates)
        {
            var resolved = Path.GetFullPath(dir);
            if (Directory.Exists(resolved))
                return resolved;
        }

        return null;
    }

    private static string? GetRuntimeSourceDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "src", "Agibuild.Fulora.Runtime"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Agibuild.Fulora.Runtime"),
        };

        foreach (var dir in candidates)
        {
            var resolved = Path.GetFullPath(dir);
            if (Directory.Exists(resolved))
                return resolved;
        }

        return null;
    }
}
