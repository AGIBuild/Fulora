// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal sealed class NSURLResponse : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSURLResponse");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_initWithUrlMimeTypeExpectedContentLengthTextEncodingName =
        Libobjc.sel_getUid("initWithURL:MIMEType:expectedContentLength:textEncodingName:");

    private NSURLResponse(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public static NSURLResponse Create(NSUrl url, string mimeType, long expectedContentLength, string? textEncodingName)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(mimeType);

        using var mimeTypeString = NSString.Create(mimeType)!;
        using var encodingString = textEncodingName is null ? null : NSString.Create(textEncodingName);
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        var handle = Libobjc.intptr_objc_msgSend(
            allocated,
            s_initWithUrlMimeTypeExpectedContentLengthTextEncodingName,
            url.Handle,
            mimeTypeString.Handle,
            expectedContentLength,
            encodingString?.Handle ?? IntPtr.Zero);

        return new NSURLResponse(handle, owns: true);
    }
}
