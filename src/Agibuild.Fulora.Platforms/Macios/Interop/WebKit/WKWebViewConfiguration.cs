// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (pattern derived from Avalonia WKWebViewConfiguration; IntPtr store/controller per T6).

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKWebViewConfiguration : NSObject
{
    private static readonly IntPtr s_class = WKWebKit.objc_getClass("WKWebViewConfiguration");
    private static readonly IntPtr s_preferences = Libobjc.sel_getUid("preferences");
    private static readonly IntPtr s_defaultWebpagePreferences = Libobjc.sel_getUid("defaultWebpagePreferences");
    private static readonly IntPtr s_websiteDataStore = Libobjc.sel_getUid("websiteDataStore");
    private static readonly IntPtr s_setWebsiteDataStore = Libobjc.sel_getUid("setWebsiteDataStore:");
    private static readonly IntPtr s_userContentController = Libobjc.sel_getUid("userContentController");
    private static readonly IntPtr s_setUserContentController = Libobjc.sel_getUid("setUserContentController:");
    private static readonly IntPtr s_setUrlSchemeHandler = Libobjc.sel_getUid("setURLSchemeHandler:forURLScheme:");
    private static readonly IntPtr s_allowsInlineMediaPlayback = Libobjc.sel_getUid("allowsInlineMediaPlayback");
    private static readonly IntPtr s_setAllowsInlineMediaPlayback = Libobjc.sel_getUid("setAllowsInlineMediaPlayback:");
    private static readonly IntPtr s_mediaTypesRequiringUserActionForPlayback = Libobjc.sel_getUid("mediaTypesRequiringUserActionForPlayback");
    private static readonly IntPtr s_setMediaTypesRequiringUserActionForPlayback = Libobjc.sel_getUid("setMediaTypesRequiringUserActionForPlayback:");

    public WKWebViewConfiguration() : base(s_class)
    {
        Init();
    }

    internal WKWebViewConfiguration(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public static WKWebViewConfiguration Create() => new WKWebViewConfiguration();

    public WKPreferences Preferences =>
        new(Libobjc.intptr_objc_msgSend(Handle, s_preferences), owns: false);

    public WKWebpagePreferences DefaultWebpagePreferences =>
        new(Libobjc.intptr_objc_msgSend(Handle, s_defaultWebpagePreferences), owns: false);

    public IntPtr WebsiteDataStore
    {
        get => Libobjc.intptr_objc_msgSend(Handle, s_websiteDataStore);
        set => Libobjc.void_objc_msgSend(Handle, s_setWebsiteDataStore, value);
    }

    public IntPtr UserContentController
    {
        get => Libobjc.intptr_objc_msgSend(Handle, s_userContentController);
        set => Libobjc.void_objc_msgSend(Handle, s_setUserContentController, value);
    }

    public void SetUrlSchemeHandler(IntPtr handler, string scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        using var schemeString = NSString.Create(scheme)!;
        Libobjc.void_objc_msgSend(Handle, s_setUrlSchemeHandler, handler, schemeString.Handle);
    }

    public bool AllowsInlineMediaPlayback
    {
        get => Libobjc.int_objc_msgSend(Handle, s_allowsInlineMediaPlayback) == 1;
        set => Libobjc.void_objc_msgSend(Handle, s_setAllowsInlineMediaPlayback, value ? 1 : 0);
    }

    public nuint MediaTypesRequiringUserActionForPlayback
    {
        get => Libobjc.nuint_objc_msgSend(Handle, s_mediaTypesRequiringUserActionForPlayback);
        set => Libobjc.void_objc_msgSend(Handle, s_setMediaTypesRequiringUserActionForPlayback, unchecked((int)(uint)value));
    }
}
