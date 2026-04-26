// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original wrapper for WebKit security origin metadata.

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKSecurityOrigin(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private static readonly IntPtr s_protocol = Libobjc.sel_getUid("protocol");
    private static readonly IntPtr s_host = Libobjc.sel_getUid("host");
    private static readonly IntPtr s_port = Libobjc.sel_getUid("port");

    public string? Protocol => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_protocol));

    public string? Host => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_host));

    public long Port => Libobjc.long_objc_msgSend(Handle, s_port);

    public string? ToOriginString()
        => FormatOrigin(Protocol, Host, Port);

    internal static string? FormatOrigin(string? scheme, string? host, long port)
    {
        scheme ??= string.Empty;
        host ??= string.Empty;

        if (scheme.Length == 0 && host.Length == 0)
        {
            return null;
        }

        var origin = $"{scheme}://{host}";
        return port > 0 ? $"{origin}:{port}" : origin;
    }

    internal static string? TryGetOriginString(IntPtr origin)
        => origin == IntPtr.Zero ? null : new WKSecurityOrigin(origin, owns: false).ToOriginString();
}
