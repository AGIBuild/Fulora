// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (selector surface aligned with WKWebViewConfiguration.preferences).

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKPreferences : NSObject
{
    private static readonly IntPtr s_javaScriptEnabled = Libobjc.sel_getUid("javaScriptEnabled");
    private static readonly IntPtr s_setJavaScriptEnabled = Libobjc.sel_getUid("setJavaScriptEnabled:");

    public WKPreferences(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public bool JavaScriptEnabled
    {
        get => Libobjc.int_objc_msgSend(Handle, s_javaScriptEnabled) == 1;
        set => Libobjc.void_objc_msgSend(Handle, s_setJavaScriptEnabled, value ? 1 : 0);
    }
}
