// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

namespace Agibuild.Fulora.Platforms.Macios.Interop.Security;

internal sealed class SecTrust : IDisposable
{
    private bool _disposed;

    internal SecTrust(IntPtr handle, bool owns)
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

    public IReadOnlyList<SecCertificate> CopyCertificateChain()
    {
        var chain = Security.SecTrustCopyCertificateChain(Handle);
        if (chain == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            var count = Security.CFArrayGetCount(chain);
            var result = new List<SecCertificate>(checked((int)count));
            for (nint i = 0; i < count; i++)
            {
                var cert = Security.CFArrayGetValueAtIndex(chain, i);
                if (cert != IntPtr.Zero)
                {
                    result.Add(new SecCertificate(Security.CFRetain(cert), owns: true));
                }
            }

            return result;
        }
        finally
        {
            Security.CFRelease(chain);
        }
    }

    public bool Evaluate(out IntPtr cfError) => Security.SecTrustEvaluateWithError(Handle, out cfError);

    public void Dispose()
    {
        if (!_disposed && Owns)
        {
            Security.CFRelease(Handle);
        }

        _disposed = true;
    }
}
