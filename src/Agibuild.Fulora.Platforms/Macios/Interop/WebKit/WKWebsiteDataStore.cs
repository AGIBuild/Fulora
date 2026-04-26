// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (Task 9).

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKWebsiteDataStore : NSObject
{
    private static readonly IntPtr s_class = WKWebKit.objc_getClass("WKWebsiteDataStore");
    private static readonly IntPtr s_defaultDataStore = Libobjc.sel_getUid("defaultDataStore");
    private static readonly IntPtr s_nonPersistentDataStore = Libobjc.sel_getUid("nonPersistentDataStore");
    private static readonly IntPtr s_httpCookieStore = Libobjc.sel_getUid("httpCookieStore");

    /// <summary>Returns the shared default data store; do not release the underlying singleton.</summary>
    public static WKWebsiteDataStore DefaultDataStore()
    {
        var h = Libobjc.intptr_objc_msgSend(s_class, s_defaultDataStore);
        return new WKWebsiteDataStore(h, owns: false);
    }

    public static WKWebsiteDataStore NonPersistentDataStore()
    {
        var h = Libobjc.intptr_objc_msgSend(s_class, s_nonPersistentDataStore);
        return new WKWebsiteDataStore(h, owns: true);
    }

    public WKWebsiteDataStore(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public WKHTTPCookieStore HttpCookieStore =>
        new(Libobjc.intptr_objc_msgSend(Handle, s_httpCookieStore), owns: false);
}
