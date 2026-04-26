// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original AppKit host-view wrapper for managed macOS adapter cutover.

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.AppKit;

[Flags]
internal enum NSViewAutoresizingMask : ulong
{
    NotSizable = 0,
    MinXMargin = 1,
    WidthSizable = 2,
    MaxXMargin = 4,
    MinYMargin = 8,
    HeightSizable = 16,
    MaxYMargin = 32
}

internal sealed class NSView(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private static readonly IntPtr s_bounds = Libobjc.sel_getUid("bounds");
    private static readonly IntPtr s_setFrame = Libobjc.sel_getUid("setFrame:");
    private static readonly IntPtr s_setAutoresizingMask = Libobjc.sel_getUid("setAutoresizingMask:");
    private static readonly IntPtr s_addSubview = Libobjc.sel_getUid("addSubview:");
    private static readonly IntPtr s_removeFromSuperview = Libobjc.sel_getUid("removeFromSuperview");

    public CGRect Bounds => Libobjc.CGRect_objc_msgSend(Handle, s_bounds);

    public CGRect Frame
    {
        set => Libobjc.void_objc_msgSend(Handle, s_setFrame, value);
    }

    public NSViewAutoresizingMask AutoresizingMask
    {
        set => Libobjc.void_objc_msgSend(Handle, s_setAutoresizingMask, (nuint)value);
    }

    public static NSView FromHandle(IntPtr handle) => new(handle, owns: false);

    public void AddSubview(NSObject child) => Libobjc.void_objc_msgSend(Handle, s_addSubview, child.Handle);

    public void RemoveFromSuperview() => Libobjc.void_objc_msgSend(Handle, s_removeFromSuperview);
}
