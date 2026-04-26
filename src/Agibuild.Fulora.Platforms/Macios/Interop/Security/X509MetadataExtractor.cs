// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Security.Cryptography.X509Certificates;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Security;

internal static class X509MetadataExtractor
{
    public static (string Subject, string Issuer, DateTimeOffset NotBefore, DateTimeOffset NotAfter)
        Extract(SecCertificate leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);

        var derBytes = leaf.CopyDer();
        using var x509 = X509CertificateLoader.LoadCertificate(derBytes);
        return (x509.Subject, x509.Issuer, x509.NotBefore, x509.NotAfter);
    }
}
