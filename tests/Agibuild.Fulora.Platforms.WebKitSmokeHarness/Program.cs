// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.InteropServices;
using System.Text.Json;
using Agibuild.Fulora;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

return WebKitSmokeHarness.Run(args);

internal static class WebKitSmokeHarness
{
    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint kCFStringEncodingUTF8 = 0x0800_0100;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static IntPtr s_cfRunLoopDefaultModeString;

    [DllImport(CoreFoundationLibrary)]
    private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, [MarshalAs(UnmanagedType.I1)] bool returnAfterSourceHandled);

    [DllImport(CoreFoundationLibrary)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr, uint encoding);

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
                case "t9-cookie-store-roundtrip":
                    RunCookieStoreRoundtrip();
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

    private static void RunCookieStoreRoundtrip()
    {
        var op = RunCookieStoreRoundtripAsync();
        while (!op.IsCompleted)
        {
            PumpMainRunLoop(TimeSpan.FromMilliseconds(100));
        }

        op.GetAwaiter().GetResult();
    }

    /// <summary>
    /// WKHTTPCookieStore completion handlers are scheduled on the main run loop; pump Core Foundation
    /// from this headless process (avoid <c>NSRunLoop</c> <c>runUntilDate:</c> here — it faulted under .NET).
    /// </summary>
    private static void PumpMainRunLoop(TimeSpan slice)
    {
        if (s_cfRunLoopDefaultModeString == IntPtr.Zero)
        {
            s_cfRunLoopDefaultModeString = CFStringCreateWithCString(IntPtr.Zero, "kCFRunLoopDefaultMode", kCFStringEncodingUTF8);
            if (s_cfRunLoopDefaultModeString == IntPtr.Zero)
            {
                throw new InvalidOperationException("CFStringCreateWithCString failed for kCFRunLoopDefaultMode.");
            }
        }

        _ = CFRunLoopRunInMode(s_cfRunLoopDefaultModeString, Math.Max(0.01, slice.TotalSeconds), true);
    }

    private static async Task RunCookieStoreRoundtripAsync()
    {
        using var store = WKWebsiteDataStore.NonPersistentDataStore();
        var cookies = store.HttpCookieStore;
        var c = NSHTTPCookie.From(new WebViewCookie("name", "value", "example.invalid", "/", null, false, false));
        await cookies.SetCookieAsync(c).ConfigureAwait(false);
        var all = await cookies.GetAllCookiesAsync().ConfigureAwait(false);
        var found = false;
        foreach (var native in all)
        {
            if (native.Name == "name" && native.Value == "value")
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            throw new InvalidOperationException("Round-trip cookie was not observed in WKHTTPCookieStore.");
        }

        await cookies.DeleteCookieAsync(c).ConfigureAwait(false);
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
