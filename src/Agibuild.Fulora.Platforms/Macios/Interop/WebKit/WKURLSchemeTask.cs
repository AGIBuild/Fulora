// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKURLSchemeTask : NSObject
{
    private static readonly IntPtr s_request = Libobjc.sel_getUid("request");
    private static readonly IntPtr s_didReceiveResponse = Libobjc.sel_getUid("didReceiveResponse:");
    private static readonly IntPtr s_didReceiveData = Libobjc.sel_getUid("didReceiveData:");
    private static readonly IntPtr s_didFinish = Libobjc.sel_getUid("didFinish");
    private static readonly IntPtr s_didFailWithError = Libobjc.sel_getUid("didFailWithError:");

    internal WKURLSchemeTask(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public NSURLRequest Request => NSURLRequest.FromHandle(Libobjc.intptr_objc_msgSend(Handle, s_request));

    public void DidReceiveResponse(NSURLResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Libobjc.void_objc_msgSend(Handle, s_didReceiveResponse, response.Handle);
    }

    public void DidReceiveData(NSData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Libobjc.void_objc_msgSend(Handle, s_didReceiveData, data.Handle);
    }

    public void DidFinish() => Libobjc.void_objc_msgSend(Handle, s_didFinish);

    public void DidFailWithError(NSError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        Libobjc.void_objc_msgSend(Handle, s_didFailWithError, error.Handle);
    }
}
