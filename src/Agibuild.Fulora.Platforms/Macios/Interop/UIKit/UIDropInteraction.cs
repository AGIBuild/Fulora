// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original UIKit drop interaction wrapper.

namespace Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

internal sealed class UIDropInteraction : NSObject
{
    private static readonly IntPtr s_class = UIKit.objc_getClass("UIDropInteraction");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_initWithDelegate = Libobjc.sel_getUid("initWithDelegate:");

    public UIDropInteraction(WKDropInteractionDelegate dropDelegate)
        : base(NewInstance(dropDelegate), owns: true)
    {
    }

    private static IntPtr NewInstance(WKDropInteractionDelegate dropDelegate)
    {
        ArgumentNullException.ThrowIfNull(dropDelegate);
        return Libobjc.intptr_objc_msgSend(
            Libobjc.intptr_objc_msgSend(s_class, s_alloc),
            s_initWithDelegate,
            dropDelegate.Handle);
    }
}
