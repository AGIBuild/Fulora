// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal sealed class NSData : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSData");
    private static readonly IntPtr s_dataWithBytes = Libobjc.sel_getUid("dataWithBytes:length:");
    private static readonly IntPtr s_bytes = Libobjc.sel_getUid("bytes");
    private static readonly IntPtr s_length = Libobjc.sel_getUid("length");

    private NSData(IntPtr handle) : base(handle, true) { }

    public static NSData FromBytes(ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* p = data)
            {
                var handle = Libobjc.intptr_objc_msgSend_intptr_nuint(
                    s_class, s_dataWithBytes, (IntPtr)p, (nuint)data.Length);
                return new NSData(handle);
            }
        }
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        var ptr = Libobjc.intptr_objc_msgSend(Handle, s_bytes);
        var len = (long)Libobjc.nuint_objc_msgSend(Handle, s_length);
        unsafe { return new ReadOnlySpan<byte>((void*)ptr, checked((int)len)); }
    }

    public byte[] ToArray() => AsSpan().ToArray();
}
