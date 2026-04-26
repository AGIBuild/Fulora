// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

/// <remarks>
/// Callers must keep a strong managed reference for as long as <c>WKUserContentController</c>
/// references this handler; the Objective-C instance stores only a weak managed handle.
/// </remarks>
internal sealed unsafe class WKScriptMessageHandler : WkDelegateBase
{
    private static readonly void* s_didReceiveScriptMessage =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidReceiveScriptMessageCallback;

    private static readonly IntPtr s_class;

    static WKScriptMessageHandler()
    {
        var cls = AllocateClassPair("ManagedWKScriptMessageHandler");
        // WebKit on current macOS exposes the userContentController:didReceiveScriptMessage:
        // contract but does not publish a WKScriptMessageHandler protocol via objc_getProtocol.
        // The WKUserContentController API dispatches by selector, so registering the method is sufficient.
        AddMethod(cls, "userContentController:didReceiveScriptMessage:", s_didReceiveScriptMessage, "v@:@@");

        if (!RegisterManagedMembers(cls))
        {
            throw new InvalidOperationException("Failed to register managed-self storage for WKScriptMessageHandler.");
        }

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    public WKScriptMessageHandler() : base(s_class)
    {
    }

    public event EventHandler<WKScriptMessageEventArgs>? DidReceiveScriptMessage;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidReceiveScriptMessageCallback(IntPtr self, IntPtr sel, IntPtr controller, IntPtr message)
    {
        var managed = ReadManagedSelf<WKScriptMessageHandler>(self);
        managed?.DidReceiveScriptMessage?.Invoke(
            managed,
            new WKScriptMessageEventArgs(new WKScriptMessage(message, owns: false)));
    }
}

internal sealed class WKScriptMessageEventArgs(WKScriptMessage message) : EventArgs
{
    public WKScriptMessage Message { get; } = message;
}
