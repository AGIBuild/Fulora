// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.InteropServices;
using System.Text;
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
                case "t12-ui-delegate-confirm":
                    RunUIDelegateConfirm();
                    break;
                case "t13-script-message":
                    RunScriptMessage();
                    break;
                case "t14-url-scheme":
                    RunUrlScheme();
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
        using var c = NSHTTPCookie.From(new WebViewCookie("name", "value", "example.invalid", "/", null, false, false));
        await cookies.SetCookieAsync(c).ConfigureAwait(false);
        var all = await cookies.GetAllCookiesAsync().ConfigureAwait(false);
        try
        {
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
        }
        finally
        {
            foreach (var native in all)
            {
                native.Dispose();
            }
        }

        await cookies.DeleteCookieAsync(c).ConfigureAwait(false);
    }

    private static void RunUIDelegateConfirm()
    {
        var op = RunUIDelegateConfirmAsync();
        while (!op.IsCompleted)
        {
            PumpMainRunLoop(TimeSpan.FromMilliseconds(100));
        }

        op.GetAwaiter().GetResult();
    }

    private static async Task RunUIDelegateConfirmAsync()
    {
        using var config = WKWebViewConfiguration.Create();
        using var webView = new WKWebView(config);
        using var ui = new WKUIDelegate();

        var messageTask = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        ui.JavaScriptConfirmPanel += (_, args) =>
        {
            messageTask.TrySetResult(args.Message);
            args.Decide(true);
        };
        webView.UIDelegate = ui;

        var evalTask = webView.EvaluateJavaScriptAsync("confirm('hello-from-test');");
        var message = await messageTask.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await evalTask.ConfigureAwait(false);

        if (message != "hello-from-test")
        {
            throw new InvalidOperationException($"Unexpected confirm message: {message}");
        }
    }

    private static void RunScriptMessage()
    {
        var op = RunScriptMessageAsync();
        while (!op.IsCompleted)
        {
            PumpMainRunLoop(TimeSpan.FromMilliseconds(100));
        }

        op.GetAwaiter().GetResult();
    }

    private static async Task RunScriptMessageAsync()
    {
        using var config = WKWebViewConfiguration.Create();
        using var ucc = new WKUserContentController();
        using var handler = new WKScriptMessageHandler();
        using var name = NSString.Create("agibuild_test")!;

        var messageTask = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.DidReceiveScriptMessage += (_, args) =>
        {
            messageTask.TrySetResult(args.Message.Name ?? string.Empty);
        };

        ucc.AddScriptMessageHandler(handler.Handle, name);
        config.UserContentController = ucc.Handle;

        using var webView = new WKWebView(config);
        var evalTask = webView.EvaluateJavaScriptAsync(
            "window.webkit.messageHandlers.agibuild_test.postMessage({hello: 'from-js'}); true;");
        var messageName = await messageTask.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        await evalTask.ConfigureAwait(false);

        if (messageName != "agibuild_test")
        {
            throw new InvalidOperationException($"Unexpected script message handler name: {messageName}");
        }
    }

    private static void RunUrlScheme()
    {
        var op = RunUrlSchemeAsync();
        while (!op.IsCompleted)
        {
            PumpMainRunLoop(TimeSpan.FromMilliseconds(100));
        }

        op.GetAwaiter().GetResult();
    }

    private static async Task RunUrlSchemeAsync()
    {
        using var config = WKWebViewConfiguration.Create();
        using var handler = new WKURLSchemeHandlerImpl();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        handler.StartTask += (_, args) =>
        {
            var bytes = Encoding.UTF8.GetBytes("<html><title>agibuild</title></html>");
            using var data = NSData.FromBytes(bytes);
            using var response = NSURLResponse.Create(args.Task.Request.Url, "text/html", bytes.Length, "utf-8");
            args.Task.DidReceiveResponse(response);
            args.Task.DidReceiveData(data);
            args.Task.DidFinish();
            started.TrySetResult();
        };

        config.SetUrlSchemeHandler(handler.Handle, "agibuild");
        using var webView = new WKWebView(config);
        using var request = NSURLRequest.FromUri(new Uri("agibuild://test/"));
        webView.Load(request);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
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
