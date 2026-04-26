// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal sealed class NSURLResponse : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSURLResponse");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_autorelease = Libobjc.sel_getUid("autorelease");
    private static readonly IntPtr s_initWithUrlMimeTypeExpectedContentLengthTextEncodingName =
        Libobjc.sel_getUid("initWithURL:MIMEType:expectedContentLength:textEncodingName:");
    private static readonly IntPtr s_url = Libobjc.sel_getUid("URL");
    private static readonly IntPtr s_mimeType = Libobjc.sel_getUid("MIMEType");
    private static readonly IntPtr s_expectedContentLength = Libobjc.sel_getUid("expectedContentLength");
    private static readonly IntPtr s_suggestedFilename = Libobjc.sel_getUid("suggestedFilename");

    internal NSURLResponse(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public static NSURLResponse Create(NSUrl url, string mimeType, long expectedContentLength, string? textEncodingName)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(mimeType);

        using var mimeTypeString = NSString.Create(mimeType)!;
        using var encodingString = textEncodingName is null ? null : NSString.Create(textEncodingName);
        var allocated = Libobjc.intptr_objc_msgSend(s_class, s_alloc);
        var initialized = Libobjc.intptr_objc_msgSend(
            allocated,
            s_initWithUrlMimeTypeExpectedContentLengthTextEncodingName,
            url.Handle,
            mimeTypeString.Handle,
            expectedContentLength,
            encodingString?.Handle ?? IntPtr.Zero);

        // NSObject(owns: true) retains on construction and releases on Dispose. Convert the
        // alloc/init +1 into an autoreleased object first so managed ownership balances to zero.
        var autoreleased = Libobjc.intptr_objc_msgSend(initialized, s_autorelease);
        return new NSURLResponse(autoreleased, owns: true);
    }

    public NSUrl? Url
    {
        get
        {
            var handle = Libobjc.intptr_objc_msgSend(Handle, s_url);
            return handle == IntPtr.Zero ? null : new NSUrl(handle, owns: false);
        }
    }

    public string? MimeType => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_mimeType));

    public long ExpectedContentLength => Libobjc.long_objc_msgSend(Handle, s_expectedContentLength);

    public string? SuggestedFilename => NSString.GetString(Libobjc.intptr_objc_msgSend(Handle, s_suggestedFilename));
}
