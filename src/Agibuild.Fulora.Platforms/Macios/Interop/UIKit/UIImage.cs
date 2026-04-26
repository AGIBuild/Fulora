// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original UIKit image wrapper for managed iOS screenshot capture.

using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

internal sealed partial class UIImage(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private const string UIKitLibrary = "/System/Library/Frameworks/UIKit.framework/UIKit";

    public NSData? ToPng()
    {
        var handle = UIImagePNGRepresentation(Handle);
        return handle == IntPtr.Zero ? null : new NSData(handle, owns: false);
    }

    [LibraryImport(UIKitLibrary)]
    private static partial IntPtr UIImagePNGRepresentation(IntPtr image);
}
