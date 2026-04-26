// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original AppKit pasteboard wrapper for managed drag/drop.

using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.AppKit;

internal sealed class NSPasteboard(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private const string TextType = "public.utf8-plain-text";
    private const string LegacyStringType = "NSStringPboardType";
    private const string HtmlType = "public.html";
    private const string UrlType = "public.url";

    private static readonly IntPtr s_stringForType = Libobjc.sel_getUid("stringForType:");
    private static readonly IntPtr s_readObjectsForClasses = Libobjc.sel_getUid("readObjectsForClasses:options:");

    public string? Text => StringForType(TextType) ?? StringForType(LegacyStringType);

    public string? Html => StringForType(HtmlType);

    public string? PasteboardUri => StringForType(UrlType);

    public IReadOnlyList<NSUrl> ReadUrls()
    {
        using var classes = NSArray.FromHandles(NSUrl.ClassHandle);
        var urlsHandle = Libobjc.intptr_objc_msgSend(
            Handle,
            s_readObjectsForClasses,
            classes.Handle,
            IntPtr.Zero);
        if (urlsHandle == IntPtr.Zero)
        {
            return [];
        }

        var urls = new NSArray(urlsHandle, owns: false);
        var count = urls.Count;
        if (count <= 0)
        {
            return [];
        }

        var result = new List<NSUrl>((int)count);
        for (nuint index = 0; index < (nuint)count; index++)
        {
            var item = urls.ObjectAtIndex(index);
            if (item != IntPtr.Zero)
            {
                result.Add(new NSUrl(item, owns: false));
            }
        }

        return result;
    }

    private string? StringForType(string type)
    {
        using var nsType = NSString.Create(type);
        return NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_stringForType, nsType.Handle));
    }
}
