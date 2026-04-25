// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Text.Json;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

return WebKitSmokeHarness.Run(args);

internal static class WebKitSmokeHarness
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static int Run(string[] args)
    {
        var caseId = ParseCaseId(args);
        if (caseId is null)
        {
            Console.Error.WriteLine("Usage: Agibuild.Fulora.Platforms.WebKitSmokeHarness --case <case-id>");
            return 64;
        }

        if (!OperatingSystem.IsMacOS())
        {
            WriteResult(caseId, ok: true, "non-macOS host skipped");
            return 0;
        }

        try
        {
            switch (caseId)
            {
                case "t6-webview-init":
                    RunWebViewInit();
                    break;
                case "t8-user-content-controller":
                    RunUserContentController();
                    break;
                default:
                    Console.Error.WriteLine($"Unknown WebKit smoke case: {caseId}");
                    return 65;
            }

            WriteResult(caseId, ok: true);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            WriteResult(caseId, ok: false, ex.GetType().FullName);
            return 1;
        }
    }

    private static void RunWebViewInit()
    {
        using var config = WKWebViewConfiguration.Create();
        using var webView = new WKWebView(config);
        if (webView.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("WKWebView handle is zero.");
        }
    }

    private static void RunUserContentController()
    {
        using var controller = new WKUserContentController();
        if (controller.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("WKUserContentController handle is zero.");
        }

        using var source = NSString.Create("// smoke")!;
        using var script = new WKUserScript(source, WKUserScriptInjectionTime.AtDocumentEnd, forMainFrameOnly: true);
        if (script.Handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("WKUserScript handle is zero.");
        }

        controller.AddUserScript(script);
        controller.RemoveAllUserScripts();
    }

    private static string? ParseCaseId(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--case")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static void WriteResult(string caseId, bool ok, string? detail = null)
    {
        var payload = detail is null
            ? new SmokeResult(caseId, ok)
            : new SmokeResult(caseId, ok, detail);
        Console.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private sealed record SmokeResult(string Case, bool Ok, string? Detail = null);
}
