using System.Reflection;
using Microsoft.Extensions.AI;

namespace Agibuild.Fulora.AI;

/// <summary>
/// Default implementation of <see cref="IAiToolRegistry"/> that uses reflection to discover
/// <see cref="AiToolAttribute"/>-marked methods and creates <see cref="AIFunction"/> wrappers.
/// </summary>
public sealed class AiToolRegistry : IAiToolRegistry
{
    private readonly Dictionary<string, AIFunction> _tools = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<AIFunction> Tools => [.. _tools.Values];

    public void Register(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var type = instance.GetType();

        foreach (var iface in type.GetInterfaces())
        {
            var ifaceAttr = iface.GetCustomAttribute<AiToolAttribute>();
            if (ifaceAttr is not null)
            {
                RegisterInterfaceMethods(instance, iface);
                continue;
            }

            foreach (var method in iface.GetMethods())
            {
                if (method.GetCustomAttribute<AiToolAttribute>() is not null)
                    RegisterMethod(instance, method);
            }
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<AiToolAttribute>() is not null && !_tools.ContainsKey(method.Name))
                RegisterMethod(instance, method);
        }
    }

    public AIFunction? FindTool(string name) =>
        _tools.GetValueOrDefault(name);

    private void RegisterInterfaceMethods(object instance, Type iface)
    {
        foreach (var method in iface.GetMethods())
        {
            if (method.IsSpecialName) continue;
            RegisterMethod(instance, method);
        }
    }

    private void RegisterMethod(object instance, MethodInfo method)
    {
        var function = AIFunctionFactory.Create(method, instance);
        _tools[function.Name] = function;
    }
}
