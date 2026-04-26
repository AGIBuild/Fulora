// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (defaultWebpagePreferences surface).

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKWebpagePreferences : NSObject
{
    private static readonly IntPtr s_allowsContentJavaScript = Libobjc.sel_getUid("allowsContentJavaScript");
    private static readonly IntPtr s_setAllowsContentJavaScript = Libobjc.sel_getUid("setAllowsContentJavaScript:");

    public WKWebpagePreferences(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public bool AllowsContentJavaScript
    {
        get => Libobjc.int_objc_msgSend(Handle, s_allowsContentJavaScript) == 1;
        set => Libobjc.void_objc_msgSend(Handle, s_setAllowsContentJavaScript, value ? 1 : 0);
    }
}
