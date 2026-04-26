// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original UIKit drop proposal wrapper.

namespace Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

internal sealed class UIDropProposal : NSObject
{
    private static readonly IntPtr s_class = UIKit.objc_getClass("UIDropProposal");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_initWithDropOperation = Libobjc.sel_getUid("initWithDropOperation:");
    private static readonly IntPtr s_autorelease = Libobjc.sel_getUid("autorelease");

    public UIDropProposal(UIDropOperation operation)
        : base(NewInstance(operation), owns: true)
    {
    }

    private static IntPtr NewInstance(UIDropOperation operation)
    {
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        var initialized = Libobjc.intptr_objc_msgSend(allocated, s_initWithDropOperation, (nint)operation);
        return Libobjc.intptr_objc_msgSend(initialized, s_autorelease);
    }
}

internal enum UIDropOperation : long
{
    Cancel = 0,
    Forbidden = 1,
    Copy = 2,
    Move = 3
}
