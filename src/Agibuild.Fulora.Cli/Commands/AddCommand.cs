using System.CommandLine;
using System.Text;

namespace Agibuild.Fulora.Cli.Commands;

internal static class AddCommand
{
    public static Command Create()
    {
        var group = new Command("add") { Description = "Add scaffolding or packages to the project" };
        group.Subcommands.Add(CreateServiceSubcommand());
        group.Subcommands.Add(AddPluginCommand.Create());
        return group;
    }

    private static Command CreateServiceSubcommand()
    {
        var nameArg = new Argument<string>("name") { Description = "Service name (PascalCase, e.g. NotificationService)" };
        var importFlag = new Option<bool>("--import") { Description = "Generate a [JsImport] interface instead of [JsExport]" };
        var layerOpt = new Option<string>("--layer")
        {
            Description = "Service ownership layer: bridge, framework, or plugin",
            Required = true
        };
        layerOpt.AcceptOnlyFromAmong("bridge", "framework", "plugin");
        var bridgeProjectOpt = new Option<string?>("--bridge-project")
        {
            Description = "Path to the Bridge .csproj (auto-detected if omitted)"
        };
        var webDirOpt = new Option<string?>("--web-dir")
        {
            Description = "Path to web project src/ directory (auto-detected if omitted)"
        };

        var command = new Command("service") { Description = "Scaffold a new bridge service (C# interface, implementation, TS proxy)" };
        command.Arguments.Add(nameArg);
        command.Options.Add(importFlag);
        command.Options.Add(layerOpt);
        command.Options.Add(bridgeProjectOpt);
        command.Options.Add(webDirOpt);

        command.SetAction((parseResult) =>
        {
            var name = parseResult.GetValue(nameArg);
            var isImport = parseResult.GetValue(importFlag);
            var layer = NormalizeLayer(parseResult.GetValue(layerOpt)!);
            var bridgeProject = parseResult.GetValue(bridgeProjectOpt);
            var webDir = parseResult.GetValue(webDirOpt);

            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("Service name is required.");
                return 1;
            }

            var interfaceName = name.StartsWith('I') ? name : $"I{name}";
            var className = interfaceName[1..];
            var camelName = char.ToLowerInvariant(className[0]) + className[1..];
            var direction = isImport ? "JsImport" : "JsExport";

            var bridgeProjDir = ResolveProjectDir(bridgeProject, "Bridge");
            if (bridgeProjDir is null)
            {
                Console.Error.WriteLine("Could not find Bridge project. Use --bridge-project to specify.");
                return 1;
            }

            var webSrcDir = webDir ?? DetectWebSrcDir(bridgeProjDir);
            if (webSrcDir is null)
            {
                Console.Error.WriteLine("Could not find web project src/ directory. Use --web-dir to specify.");
                return 1;
            }

            // C# interface
            var interfaceTargetDir = ResolveBridgeLayerDir(bridgeProjDir, layer);
            Directory.CreateDirectory(interfaceTargetDir);
            var interfacePath = Path.Combine(interfaceTargetDir, $"{interfaceName}.cs");
            var interfaceNamespace = InferNamespace(interfaceTargetDir);
            if (!File.Exists(interfacePath))
            {
                File.WriteAllText(interfacePath, GenerateCSharpInterface(interfaceNamespace, interfaceName, direction), Encoding.UTF8);
                Console.WriteLine($"Created {interfacePath}");
            }
            else
            {
                Console.WriteLine($"Skipped {interfacePath} (already exists)");
            }

            // C# implementation (only for JsExport)
            if (!isImport)
            {
                var implTargetDir = ResolveImplementationTargetDir(bridgeProjDir, layer);
                Directory.CreateDirectory(implTargetDir);

                var implPath = Path.Combine(implTargetDir, $"{className}.cs");
                if (!File.Exists(implPath))
                {
                    var ns = InferNamespace(implTargetDir);
                    File.WriteAllText(implPath, GenerateCSharpImplementation(className, interfaceName, ns, interfaceNamespace), Encoding.UTF8);
                    Console.WriteLine($"Created {implPath}");
                }
                else
                {
                    Console.WriteLine($"Skipped {implPath} (already exists)");
                }
            }

            // TypeScript proxy
            var bridgeDir = Path.Combine(webSrcDir, "bridge");
            Directory.CreateDirectory(bridgeDir);
            var tsPath = Path.Combine(bridgeDir, $"{camelName}.ts");
            if (!File.Exists(tsPath))
            {
                File.WriteAllText(tsPath, GenerateTypeScriptProxy(className, camelName), Encoding.UTF8);
                Console.WriteLine($"Created {tsPath}");
            }
            else
            {
                Console.WriteLine($"Skipped {tsPath} (already exists)");
            }

            Console.WriteLine();
            Console.WriteLine($"Service '{className}' scaffolded successfully in layer '{layer}'.");
            if (!isImport)
            {
                Console.WriteLine($"Wire it up: Bridge.Expose<{interfaceName}>(new {className}());");
            }
            return 0;
        });

        return command;
    }

    private static string GenerateCSharpInterface(string ns, string interfaceName, string attribute) =>
        $$"""
        namespace {{ns}};

        [{{attribute}}]
        public interface {{interfaceName}}
        {
            Task<string> Ping();
        }
        """;

    private static string GenerateCSharpImplementation(string className, string interfaceName, string ns, string interfaceNamespace) =>
        $$"""
        using {{interfaceNamespace}};

        namespace {{ns}};

        public sealed class {{className}} : {{interfaceName}}
        {
            public Task<string> Ping() => Task.FromResult("pong");
        }
        """;

    private static string GenerateTypeScriptProxy(string className, string camelName) =>
        $$"""
        import { bridge } from "./client";

        export interface {{className}} {
          ping(): Promise<string>;
        }

        export const {{camelName}} = bridge.getService<{{className}}>("{{className}}");
        """;

    private static string NormalizeLayer(string layer)
        => layer.Trim().ToLowerInvariant();

    private static string ResolveBridgeLayerDir(string bridgeProjDir, string layer)
        => layer switch
        {
            "bridge" => bridgeProjDir,
            "framework" => Path.Combine(bridgeProjDir, "Framework"),
            "plugin" => Path.Combine(bridgeProjDir, "Plugins"),
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unsupported service layer.")
        };

    private static string ResolveImplementationTargetDir(string bridgeProjDir, string layer)
        => layer switch
        {
            "bridge" => bridgeProjDir,
            "framework" => Path.Combine(ResolveDesktopProjectDir(bridgeProjDir), "Framework"),
            "plugin" => Path.Combine(ResolveDesktopProjectDir(bridgeProjDir), "Plugins"),
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unsupported service layer.")
        };

    private static string? ResolveProjectDir(string? explicitPath, string hint)
    {
        if (explicitPath is not null)
        {
            return File.Exists(explicitPath)
                ? Path.GetDirectoryName(explicitPath)
                : (Directory.Exists(explicitPath) ? explicitPath : null);
        }

        var cwd = Directory.GetCurrentDirectory();
        var candidates = Directory.GetDirectories(cwd, $"*.{hint}", SearchOption.AllDirectories)
            .Concat(Directory.GetDirectories(cwd, $"*{hint}*", SearchOption.AllDirectories))
            .Where(d => !d.Contains("bin") && !d.Contains("obj") && !d.Contains("node_modules"))
            .Distinct()
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            > 1 => candidates.FirstOrDefault(p => Path.GetFileName(p).Contains(hint, StringComparison.OrdinalIgnoreCase)),
            _ => null,
        };
    }

    private static string? DetectWebSrcDir(string bridgeProjDir)
    {
        var parent = Directory.GetParent(bridgeProjDir)?.FullName;
        if (parent is null) return null;

        var webDirs = Directory.GetDirectories(parent)
            .Where(d =>
            {
                var name = Path.GetFileName(d);
                return name.Contains("Web", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("Vite", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (webDirs.Length == 0)
            return null;

        var srcDir = Path.Combine(webDirs[0], "src");
        return Directory.Exists(srcDir) ? srcDir : webDirs[0];
    }

    private static string InferNamespace(string dir)
    {
        var current = dir;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var csproj = Directory.GetFiles(current, "*.csproj").FirstOrDefault();
            if (csproj is not null)
            {
                var rootNamespace = Path.GetFileNameWithoutExtension(csproj);
                var relative = Path.GetRelativePath(current, dir);
                if (relative == ".")
                    return rootNamespace;

                var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizeNamespaceSegment);
                return string.Join(".", new[] { rootNamespace }.Concat(segments));
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        return Path.GetFileName(dir) ?? "HybridApp.Desktop";
    }

    private static string ResolveDesktopProjectDir(string bridgeProjDir)
    {
        var parent = Directory.GetParent(bridgeProjDir)?.FullName;
        if (parent is null)
            throw new DirectoryNotFoundException("Could not resolve Desktop project from Bridge project.");

        var desktopDirs = Directory.GetDirectories(parent)
            .Where(d => Path.GetFileName(d).Contains("Desktop", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return desktopDirs.Length switch
        {
            1 => desktopDirs[0],
            > 1 => desktopDirs[0],
            _ => throw new DirectoryNotFoundException("Could not find Desktop project alongside Bridge project.")
        };
    }

    private static string SanitizeNamespaceSegment(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        foreach (var ch in segment)
            builder.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

        return builder.Length == 0 ? "_" : builder.ToString();
    }
}
