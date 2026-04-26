// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.InteropServices;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Security;

internal static partial class Security
{
    private const string Lib = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [LibraryImport(Lib)]
    internal static partial IntPtr SecCertificateCreateWithData(IntPtr allocator, IntPtr data);

    [LibraryImport(Lib)]
    internal static partial IntPtr SecTrustCopyCertificateChain(IntPtr trust);

    [LibraryImport(Lib)]
    internal static partial IntPtr SecCertificateCopySubjectSummary(IntPtr cert);

    [LibraryImport(Lib)]
    internal static partial IntPtr SecCertificateCopyData(IntPtr cert);

    [LibraryImport(Lib)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool SecTrustEvaluateWithError(IntPtr trust, out IntPtr cfErrorOut);

    [LibraryImport(CoreFoundation)]
    internal static partial void CFRelease(IntPtr cf);

    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFRetain(IntPtr cf);

    [LibraryImport(CoreFoundation)]
    internal static partial long CFErrorGetCode(IntPtr err);

    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFErrorCopyDescription(IntPtr err);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFArrayGetCount(IntPtr array);

    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [LibraryImport(CoreFoundation)]
    internal static partial nint CFDataGetLength(IntPtr data);

    [LibraryImport(CoreFoundation)]
    internal static partial IntPtr CFDataGetBytePtr(IntPtr data);
}
