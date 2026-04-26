// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (selector surface aligned with legacy Apple shim behavior).

using System;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKUserContentController : NSObject
{
    private static readonly IntPtr s_class = WKWebKit.objc_getClass("WKUserContentController");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_init = Libobjc.sel_getUid("init");
    private static readonly IntPtr s_addUserScript = Libobjc.sel_getUid("addUserScript:");
    private static readonly IntPtr s_removeAllUserScripts = Libobjc.sel_getUid("removeAllUserScripts");
    private static readonly IntPtr s_addScriptMessageHandler = Libobjc.sel_getUid("addScriptMessageHandler:name:");
    private static readonly IntPtr s_removeScriptMessageHandlerForName =
        Libobjc.sel_getUid("removeScriptMessageHandlerForName:");

    public WKUserContentController() : base(NewInstance(), owns: true)
    {
    }

    private static IntPtr NewInstance()
    {
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        return Libobjc.intptr_objc_msgSend(allocated, s_init);
    }

    public void AddUserScript(WKUserScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Libobjc.void_objc_msgSend(Handle, s_addUserScript, script.Handle);
    }

    public void RemoveAllUserScripts() => Libobjc.void_objc_msgSend(Handle, s_removeAllUserScripts);

    public void AddScriptMessageHandler(IntPtr handler, NSString name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (handler == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        Libobjc.void_objc_msgSend(Handle, s_addScriptMessageHandler, handler, name.Handle);
    }

    public void RemoveScriptMessageHandlerForName(NSString name)
    {
        ArgumentNullException.ThrowIfNull(name);
        Libobjc.void_objc_msgSend(Handle, s_removeScriptMessageHandlerForName, name.Handle);
    }
}
