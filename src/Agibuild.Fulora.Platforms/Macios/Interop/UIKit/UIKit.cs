// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original UIKit interop used by managed iOS adapter cutover.

namespace Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

internal static partial class UIKit
{
    private const int RTLD_LAZY = 1;
    private const string UIKitDylib = "/System/Library/Frameworks/UIKit.framework/UIKit";

    static UIKit()
    {
        if (OperatingSystem.IsIOS())
        {
            _ = Libobjc.dlopen(UIKitDylib, RTLD_LAZY);
        }
    }

    public static IntPtr objc_getClass(string className) => Libobjc.objc_getClass(className);
}
