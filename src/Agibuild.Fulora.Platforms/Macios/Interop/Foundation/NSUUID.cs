// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.

using System;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal class NSUUID : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSUUID");
    private static readonly IntPtr s_initWithUUIDBytes = Libobjc.sel_getUid("initWithUUIDBytes:");

    private NSUUID() : base(s_class)
    {
    }

    private NSUUID(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public static unsafe NSUUID Create(Guid value)
    {
        const int size = 16;
        var buffer = stackalloc byte[size];
        _ = value.TryWriteBytes(new Span<byte>(buffer, size), true, out _);

        var uuid = new NSUUID();
        _ = Libobjc.intptr_objc_msgSend(uuid.Handle, s_initWithUUIDBytes, new IntPtr(buffer));
        return uuid;
    }
}
