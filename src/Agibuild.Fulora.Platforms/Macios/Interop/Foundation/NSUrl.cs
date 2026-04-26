// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.

using System;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal class NSUrl : NSObject
{
    private static readonly IntPtr s_class = Foundation.objc_getClass("NSURL");
    private static readonly IntPtr s_createWithUrl = Libobjc.sel_getUid("URLWithString:");
    private static readonly IntPtr s_absoluteString = Libobjc.sel_getUid("absoluteString");
    private static readonly IntPtr s_isFileUrl = Libobjc.sel_getUid("isFileURL");
    private static readonly IntPtr s_path = Libobjc.sel_getUid("path");

    public NSUrl(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public NSUrl(NSString nsString) : this(Libobjc.intptr_objc_msgSend(s_class, s_createWithUrl, nsString.Handle), true)
    {
    }

    public string? AbsoluteString
    {
        get
        {
            var nsString = Libobjc.intptr_objc_msgSend(Handle, s_absoluteString);
            return NSString.GetString(nsString);
        }
    }

    public static IntPtr ClassHandle => s_class;

    public bool IsFileUrl => Libobjc.int_objc_msgSend(Handle, s_isFileUrl) == 1;

    public string? Path => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_path));
}
