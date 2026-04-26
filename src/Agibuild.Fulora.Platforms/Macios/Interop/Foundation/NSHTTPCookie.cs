// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView (NSHTTPCookie); see Macios/ATTRIBUTION.md.
// Extended with WebViewCookie conversions (Fulora Task 9).

using System;
using System.Collections.Generic;
using Agibuild.Fulora;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal sealed class NSHTTPCookie : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSHTTPCookie");
    private static readonly IntPtr s_cookieWithProperties = Libobjc.sel_getUid("cookieWithProperties:");
    private static readonly IntPtr s_name = Libobjc.sel_getUid("name");
    private static readonly IntPtr s_value = Libobjc.sel_getUid("value");
    private static readonly IntPtr s_domain = Libobjc.sel_getUid("domain");
    private static readonly IntPtr s_path = Libobjc.sel_getUid("path");
    private static readonly IntPtr s_expiresDate = Libobjc.sel_getUid("expiresDate");
    private static readonly IntPtr s_isSecure = Libobjc.sel_getUid("isSecure");
    private static readonly IntPtr s_isHTTPOnly = Libobjc.sel_getUid("isHTTPOnly");

    public NSHTTPCookie(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public string Name => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_name)) ?? string.Empty;

    public string Value => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_value)) ?? string.Empty;

    public static NSHTTPCookie From(WebViewCookie cookie)
    {
        if (string.IsNullOrEmpty(cookie.Name))
            throw new ArgumentException("Cookie name is required.", nameof(cookie));
        if (string.IsNullOrEmpty(cookie.Domain))
            throw new ArgumentException("Cookie domain is required.", nameof(cookie));

        var objects = new List<NSObject>();
        var keys = new List<NSObject>();

        using var nameKey = NSString.Create("Name")!;
        using var valueKey = NSString.Create("Value")!;
        using var domainKey = NSString.Create("Domain")!;
        using var pathKey = NSString.Create("Path")!;
        using var secureKey = NSString.Create("Secure")!;
        using var httpOnlyKey = NSString.Create("HttpOnly")!;
        using var expiresKey = NSString.Create("Expires")!;

        using var vName = NSString.Create(cookie.Name)!;
        using var vValue = NSString.Create(cookie.Value)!;
        using var vDomain = NSString.Create(cookie.Domain)!;
        using var vPath = NSString.Create(string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path)!;

        objects.Add(vName);
        keys.Add(nameKey);
        objects.Add(vValue);
        keys.Add(valueKey);
        objects.Add(vDomain);
        keys.Add(domainKey);
        objects.Add(vPath);
        keys.Add(pathKey);

        NSString? secureValue = null;
        NSString? httpOnlyValue = null;
        NSDate? expiresValue = null;
        try
        {
            if (cookie.IsSecure)
            {
                secureValue = NSString.Create("TRUE")!;
                objects.Add(secureValue);
                keys.Add(secureKey);
            }

            if (cookie.IsHttpOnly)
            {
                httpOnlyValue = NSString.Create("TRUE")!;
                objects.Add(httpOnlyValue);
                keys.Add(httpOnlyKey);
            }

            if (cookie.Expires is { } exp)
            {
                expiresValue = NSDate.FromDateTimeOffset(exp);
                objects.Add(expiresValue);
                keys.Add(expiresKey);
            }

            using var dict = NSDictionary.WithObjects(objects, keys, (uint)keys.Count);
            var handle = Libobjc.intptr_objc_msgSend(s_class, s_cookieWithProperties, dict.Handle);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException("NSHTTPCookie creation failed (cookieWithProperties: returned null).");

            return new NSHTTPCookie(handle, owns: true);
        }
        finally
        {
            secureValue?.Dispose();
            httpOnlyValue?.Dispose();
            expiresValue?.Dispose();
        }
    }

    public WebViewCookie ToWebViewCookie()
    {
        var name = Name;
        var value = Value;
        var domain = NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_domain)) ?? string.Empty;
        var path = NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_path)) ?? "/";

        DateTimeOffset? expires = null;
        var expiresPtr = Libobjc.intptr_objc_msgSend(Handle, s_expiresDate);
        if (expiresPtr != IntPtr.Zero)
        {
            using var date = new NSDate(expiresPtr, owns: false);
            expires = date.ToDateTimeOffset();
        }

        var isSecure = Libobjc.int_objc_msgSend(Handle, s_isSecure) == 1;
        var isHttpOnly = Libobjc.int_objc_msgSend(Handle, s_isHTTPOnly) == 1;

        return new WebViewCookie(name, value, domain, path, expires, isSecure, isHttpOnly);
    }
}
