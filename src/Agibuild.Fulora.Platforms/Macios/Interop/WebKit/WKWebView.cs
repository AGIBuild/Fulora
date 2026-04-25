// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (selector subset from WkWebViewShim.mm; evaluateJavaScript block pattern from Avalonia WKWebView).

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKWebView : NSObject
{
    private static readonly IntPtr s_class = WKWebKit.objc_getClass("WKWebView");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_initWithFrameConfiguration = Libobjc.sel_getUid("initWithFrame:configuration:");
    private static readonly IntPtr s_loadHTMLString = Libobjc.sel_getUid("loadHTMLString:baseURL:");
    private static readonly IntPtr s_url = Libobjc.sel_getUid("URL");
    private static readonly IntPtr s_canGoBack = Libobjc.sel_getUid("canGoBack");
    private static readonly IntPtr s_goBack = Libobjc.sel_getUid("goBack");
    private static readonly IntPtr s_canGoForward = Libobjc.sel_getUid("canGoForward");
    private static readonly IntPtr s_goForward = Libobjc.sel_getUid("goForward");
    private static readonly IntPtr s_reload = Libobjc.sel_getUid("reload");
    private static readonly IntPtr s_stopLoading = Libobjc.sel_getUid("stopLoading");
    private static readonly IntPtr s_evaluateJavaScript = Libobjc.sel_getUid("evaluateJavaScript:completionHandler:");
    private static readonly IntPtr s_isKindOfClass = Libobjc.sel_getUid("isKindOfClass:");
    private static readonly unsafe IntPtr s_evaluateScriptCallback = new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&EvaluateJavaScriptTrampoline);

    public WKWebView(WKWebViewConfiguration configuration) : base(NewInstance(configuration), owns: true)
    {
    }

    private static IntPtr NewInstance(WKWebViewConfiguration configuration)
    {
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        var frame = new CGRect(0, 0, 0, 0);
        return Libobjc.intptr_objc_msgSend(allocated, s_initWithFrameConfiguration, frame, configuration.Handle);
    }

    public NSUrl? Url
    {
        get
        {
            var h = Libobjc.intptr_objc_msgSend(Handle, s_url);
            return h == IntPtr.Zero ? null : new NSUrl(h, owns: false);
        }
    }

    public bool CanGoBack => Libobjc.int_objc_msgSend(Handle, s_canGoBack) == 1;

    public bool CanGoForward => Libobjc.int_objc_msgSend(Handle, s_canGoForward) == 1;

    public void GoBack() => _ = Libobjc.intptr_objc_msgSend(Handle, s_goBack);

    public void GoForward() => _ = Libobjc.intptr_objc_msgSend(Handle, s_goForward);

    public void Reload() => _ = Libobjc.intptr_objc_msgSend(Handle, s_reload);

    public void Stop() => Libobjc.void_objc_msgSend(Handle, s_stopLoading);

    public void LoadHTMLString(string html, NSUrl? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        using var htmlNs = NSString.Create(html)!;
        Libobjc.void_objc_msgSend(Handle, s_loadHTMLString, htmlNs.Handle, baseUrl?.Handle ?? IntPtr.Zero);
    }

    public async Task<NSObject?> EvaluateJavaScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var tcs = new TaskCompletionSource<NSObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new JSEvalState(tcs);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            using var scriptStr = NSString.Create(script)!;
            var block = BlockLiteral.GetBlockForFunctionPointer(s_evaluateScriptCallback, GCHandle.ToIntPtr(stateHandle));
            Libobjc.void_objc_msgSend(Handle, s_evaluateJavaScript, scriptStr.Handle, block);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }
        }
    }

    private sealed record JSEvalState(TaskCompletionSource<NSObject?> Tcs);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void EvaluateJavaScriptTrampoline(IntPtr block, IntPtr value, IntPtr nsError)
    {
        var statePtr = BlockLiteral.TryGetBlockState(block);
        if (statePtr == IntPtr.Zero)
        {
            return;
        }

        if (GCHandle.FromIntPtr(statePtr).Target is not JSEvalState state)
        {
            return;
        }

        if (nsError != IntPtr.Zero)
        {
            _ = state.Tcs.TrySetException(NSError.ToException(nsError));
            return;
        }

        _ = state.Tcs.TrySetResult(WrapJsResult(value));
    }

    private static NSObject? WrapJsResult(IntPtr value)
    {
        if (value == IntPtr.Zero || IsNSNull(value))
        {
            return null;
        }

        if (NSString.TryGetString(value) is not null)
        {
            return NSString.FromHandle(value);
        }

        return new ObjCId(value, owns: false);
    }

    private static bool IsNSNull(IntPtr value)
    {
        var nsNullClass = Libobjc.objc_getClass("NSNull");
        return Libobjc.int_objc_msgSend(value, s_isKindOfClass, nsNullClass) == 1;
    }

    private sealed class ObjCId : NSObject
    {
        public ObjCId(IntPtr handle, bool owns) : base(handle, owns)
        {
        }
    }
}
