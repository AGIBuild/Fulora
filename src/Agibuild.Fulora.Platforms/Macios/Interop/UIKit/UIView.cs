// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original UIKit host-view wrapper for managed iOS adapter cutover.

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

[Flags]
internal enum UIViewAutoresizing : ulong
{
    None = 0,
    FlexibleLeftMargin = 1,
    FlexibleWidth = 2,
    FlexibleRightMargin = 4,
    FlexibleTopMargin = 8,
    FlexibleHeight = 16,
    FlexibleBottomMargin = 32
}

internal sealed class UIView(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private static readonly IntPtr s_bounds = Libobjc.sel_getUid("bounds");
    private static readonly IntPtr s_setFrame = Libobjc.sel_getUid("setFrame:");
    private static readonly IntPtr s_setAutoresizingMask = Libobjc.sel_getUid("setAutoresizingMask:");
    private static readonly IntPtr s_addSubview = Libobjc.sel_getUid("addSubview:");
    private static readonly IntPtr s_removeFromSuperview = Libobjc.sel_getUid("removeFromSuperview");
    private static readonly IntPtr s_addInteraction = Libobjc.sel_getUid("addInteraction:");
    private static readonly IntPtr s_removeInteraction = Libobjc.sel_getUid("removeInteraction:");

    public CGRect Bounds => Libobjc.CGRect_objc_msgSend(Handle, s_bounds);

    public CGRect Frame
    {
        set => Libobjc.void_objc_msgSend(Handle, s_setFrame, value);
    }

    public UIViewAutoresizing AutoresizingMask
    {
        set => Libobjc.void_objc_msgSend(Handle, s_setAutoresizingMask, (nuint)value);
    }

    public static UIView FromHandle(IntPtr handle) => new(handle, owns: false);

    public void AddSubview(NSObject child) => Libobjc.void_objc_msgSend(Handle, s_addSubview, child.Handle);

    public void RemoveFromSuperview() => Libobjc.void_objc_msgSend(Handle, s_removeFromSuperview);

    public void AddInteraction(NSObject interaction) => Libobjc.void_objc_msgSend(Handle, s_addInteraction, interaction.Handle);

    public void RemoveInteraction(NSObject interaction) => Libobjc.void_objc_msgSend(Handle, s_removeInteraction, interaction.Handle);
}
