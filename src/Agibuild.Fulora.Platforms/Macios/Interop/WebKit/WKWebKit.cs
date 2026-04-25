// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView (WebKit.cs); see Macios/ATTRIBUTION.md.
// AMENDMENT #8: static cctor dlopen + protocol forwarders use Libobjc after framework load.

using System;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal static class WKWebKit
{
    private const int RTLD_LAZY = 0x1;
    private const string WebKitDylib = "/System/Library/Frameworks/WebKit.framework/WebKit";

    static WKWebKit()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS())
        {
            _ = Libobjc.dlopen(WebKitDylib, RTLD_LAZY);
        }
    }

    internal static IntPtr objc_getClass(string className) => Libobjc.objc_getClass(className);

    internal static IntPtr objc_getProtocol(string name) => Libobjc.objc_getProtocol(name);
}
