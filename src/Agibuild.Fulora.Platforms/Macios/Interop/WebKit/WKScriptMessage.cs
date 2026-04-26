// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKScriptMessage : NSObject
{
    private static readonly IntPtr s_name = Libobjc.sel_getUid("name");
    private static readonly IntPtr s_body = Libobjc.sel_getUid("body");
    private static readonly IntPtr s_frameInfo = Libobjc.sel_getUid("frameInfo");
    private static readonly IntPtr s_world = Libobjc.sel_getUid("world");

    internal WKScriptMessage(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public string? Name => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_name));

    public IntPtr Body => Libobjc.intptr_objc_msgSend(Handle, s_body);

    public IntPtr FrameInfo => Libobjc.intptr_objc_msgSend(Handle, s_frameInfo);

    public IntPtr World => Libobjc.intptr_objc_msgSend(Handle, s_world);
}
