// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original AppKit color wrapper for managed macOS adapter cutover.

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.AppKit;

internal sealed class NSColor(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private static readonly IntPtr s_class = AppKit.objc_getClass("NSColor");
    private static readonly IntPtr s_clearColor = Libobjc.sel_getUid("clearColor");
    private static readonly IntPtr s_controlBackgroundColor = Libobjc.sel_getUid("controlBackgroundColor");

    public static NSColor ClearColor => new(Libobjc.intptr_objc_msgSend(s_class, s_clearColor), owns: false);

    public static NSColor ControlBackgroundColor => new(Libobjc.intptr_objc_msgSend(s_class, s_controlBackgroundColor), owns: false);
}
