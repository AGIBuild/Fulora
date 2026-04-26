// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (initWithSource:injectionTime:forMainFrameOnly:).

using System;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal enum WKUserScriptInjectionTime : long
{
    AtDocumentStart = 0,
    AtDocumentEnd = 1,
}

internal sealed class WKUserScript : NSObject
{
    private static readonly IntPtr s_class = WKWebKit.objc_getClass("WKUserScript");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_initWithSource =
        Libobjc.sel_getUid("initWithSource:injectionTime:forMainFrameOnly:");

    public WKUserScript(NSString source, WKUserScriptInjectionTime injectionTime, bool forMainFrameOnly)
        : base(NewInstance(source, injectionTime, forMainFrameOnly), owns: true)
    {
    }

    private static IntPtr NewInstance(NSString source, WKUserScriptInjectionTime injectionTime, bool forMainFrameOnly)
    {
        ArgumentNullException.ThrowIfNull(source);
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        return Libobjc.intptr_objc_msgSend(
            allocated,
            s_initWithSource,
            source.Handle,
            new IntPtr((nint)injectionTime),
            forMainFrameOnly ? 1 : 0);
    }
}
