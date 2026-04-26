// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original AppKit image wrapper for managed screenshot capture.

using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.AppKit;

internal sealed class NSImage(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private static readonly IntPtr s_tiffRepresentation = Libobjc.sel_getUid("TIFFRepresentation");

    public NSData? TiffRepresentation
    {
        get
        {
            var handle = Libobjc.intptr_objc_msgSend(Handle, s_tiffRepresentation);
            return handle == IntPtr.Zero ? null : new NSData(handle, owns: false);
        }
    }
}
