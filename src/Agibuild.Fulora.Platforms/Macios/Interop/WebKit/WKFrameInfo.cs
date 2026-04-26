// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original wrapper for WebKit frame metadata.

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKFrameInfo(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private static readonly IntPtr s_securityOrigin = Libobjc.sel_getUid("securityOrigin");

    public string? Origin
    {
        get
        {
            var origin = Libobjc.intptr_objc_msgSend(Handle, s_securityOrigin);
            return WKSecurityOrigin.TryGetOriginString(origin);
        }
    }

    internal static string? TryGetOriginString(IntPtr frame)
        => frame == IntPtr.Zero ? null : new WKFrameInfo(frame, owns: false).Origin;
}
