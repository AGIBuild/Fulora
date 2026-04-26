// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

/// <summary>
/// Managed wrapper around an Objective-C <c>NSData</c> instance.
/// </summary>
/// <remarks>
/// <para>
/// Lifetime contract: the unmanaged buffer that backs <see cref="AsSpan"/> is owned by the
/// underlying <c>NSData</c> object. The buffer is freed when this wrapper is disposed (or
/// finalized) and the wrapped <c>NSData</c> is <c>release</c>d. Callers MUST keep the
/// <see cref="NSData"/> instance reachable for the entire span/pointer usage window;
/// using the span after disposal is undefined behaviour (use-after-free).
/// </para>
/// <para>
/// If you need to outlive the <see cref="NSData"/>, copy via <see cref="ToArray"/>.
/// </para>
/// </remarks>
internal sealed class NSData : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSData");
    private static readonly IntPtr s_dataWithBytes = Libobjc.sel_getUid("dataWithBytes:length:");
    private static readonly IntPtr s_bytes = Libobjc.sel_getUid("bytes");
    private static readonly IntPtr s_length = Libobjc.sel_getUid("length");

    internal NSData(IntPtr handle, bool owns) : base(handle, owns) { }

    /// <summary>
    /// Creates a new <see cref="NSData"/> by copying <paramref name="data"/> into an
    /// Objective-C-owned buffer (via <c>+[NSData dataWithBytes:length:]</c>).
    /// </summary>
    /// <remarks>
    /// The returned wrapper is <c>retain</c>ed (<c>owns: true</c>) so it balances the
    /// autoreleased factory; the input <paramref name="data"/> span is not retained beyond
    /// the call.
    /// </remarks>
    public static NSData FromBytes(ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* p = data)
            {
                var handle = Libobjc.intptr_objc_msgSend_intptr_nuint(
                    s_class, s_dataWithBytes, (IntPtr)p, (nuint)data.Length);
                return new NSData(handle, owns: true);
            }
        }
    }

    /// <summary>
    /// Returns a span over the underlying Objective-C-owned buffer.
    /// </summary>
    /// <remarks>
    /// See type-level remarks for the lifetime contract — the span is invalidated when this
    /// <see cref="NSData"/> is disposed. The returned span length is bounded by
    /// <see cref="int.MaxValue"/>; payloads larger than 2 GiB throw <see cref="OverflowException"/>
    /// (use a chunked or stream-based path for such sizes).
    /// </remarks>
    public ReadOnlySpan<byte> AsSpan()
    {
        var ptr = Libobjc.intptr_objc_msgSend(Handle, s_bytes);
        var len = (long)Libobjc.nuint_objc_msgSend(Handle, s_length);
        unsafe { return new ReadOnlySpan<byte>((void*)ptr, checked((int)len)); }
    }

    /// <summary>
    /// Copies the underlying buffer into a freshly-allocated managed array, decoupling the
    /// caller from this <see cref="NSData"/>'s lifetime.
    /// </summary>
    public byte[] ToArray() => AsSpan().ToArray();
}
