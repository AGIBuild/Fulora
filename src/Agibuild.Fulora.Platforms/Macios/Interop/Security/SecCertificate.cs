// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Security;

internal sealed class SecCertificate : IDisposable
{
    private bool _disposed;

    internal SecCertificate(IntPtr handle, bool owns)
    {
        if (handle == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(handle));
        }

        Handle = handle;
        Owns = owns;
    }

    public IntPtr Handle { get; }

    private bool Owns { get; }

    public static SecCertificate FromDer(ReadOnlySpan<byte> derBytes)
    {
        using var data = NSData.FromBytes(derBytes);
        var handle = Security.SecCertificateCreateWithData(IntPtr.Zero, data.Handle);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("SecCertificateCreateWithData returned null.");
        }

        return new SecCertificate(handle, owns: true);
    }

    public byte[] CopyDer()
    {
        var data = Security.SecCertificateCopyData(Handle);
        if (data == IntPtr.Zero)
        {
            throw new InvalidOperationException("SecCertificateCopyData returned null.");
        }

        try
        {
            var length = checked((int)Security.CFDataGetLength(data));
            var bytes = new byte[length];
            unsafe
            {
                var ptr = (byte*)Security.CFDataGetBytePtr(data);
                new ReadOnlySpan<byte>(ptr, length).CopyTo(bytes);
            }

            return bytes;
        }
        finally
        {
            Security.CFRelease(data);
        }
    }

    public void Dispose()
    {
        if (!_disposed && Owns)
        {
            Security.CFRelease(Handle);
        }

        _disposed = true;
    }
}
