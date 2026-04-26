// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original AppKit bitmap wrapper for managed screenshot capture.

using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.AppKit;

internal sealed class NSBitmapImageRep : NSObject
{
    private const int NSBitmapImageFileTypePng = 4;

    private static readonly IntPtr s_class = AppKit.objc_getClass("NSBitmapImageRep");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_autorelease = Libobjc.sel_getUid("autorelease");
    private static readonly IntPtr s_initWithData = Libobjc.sel_getUid("initWithData:");
    private static readonly IntPtr s_representationUsingTypeProperties =
        Libobjc.sel_getUid("representationUsingType:properties:");

    private NSBitmapImageRep(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public static NSBitmapImageRep? FromTiff(NSData data)
    {
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        var initialized = Libobjc.intptr_objc_msgSend(allocated, s_initWithData, data.Handle);
        if (initialized == IntPtr.Zero)
        {
            return null;
        }

        var autoreleased = Libobjc.intptr_objc_msgSend(initialized, s_autorelease);
        return new NSBitmapImageRep(autoreleased, owns: true);
    }

    public NSData? ToPng()
    {
        using var properties = NSDictionary.Empty;
        var handle = Libobjc.intptr_objc_msgSend(
            Handle,
            s_representationUsingTypeProperties,
            NSBitmapImageFileTypePng,
            properties.Handle);
        return handle == IntPtr.Zero ? null : new NSData(handle, owns: false);
    }
}
