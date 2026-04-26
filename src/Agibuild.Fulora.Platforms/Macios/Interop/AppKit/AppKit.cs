// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original AppKit interop used by managed macOS adapter cutover.

namespace Agibuild.Fulora.Platforms.Macios.Interop.AppKit;

internal static class AppKit
{
    private const int RTLD_LAZY = 1;
    private const string AppKitDylib = "/System/Library/Frameworks/AppKit.framework/AppKit";

    static AppKit()
    {
        if (OperatingSystem.IsMacOS())
        {
            _ = Libobjc.dlopen(AppKitDylib, RTLD_LAZY);
        }
    }

    public static IntPtr objc_getClass(string className) => Libobjc.objc_getClass(className);
}
