// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Threading;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal abstract unsafe class WkDelegateBase(IntPtr classHandle) : NSManagedObjectBase(classHandle)
{
    protected static void AddProtocol(IntPtr cls, string protocolName)
    {
        var protocol = WKWebKit.objc_getProtocol(protocolName);
        if (protocol == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Objective-C protocol was not found: {protocolName}");
        }

        if (Libobjc.class_addProtocol(cls, protocol) != 1)
        {
            throw new InvalidOperationException($"Failed to add Objective-C protocol: {protocolName}");
        }
    }

    protected static void AddMethod(IntPtr cls, string selector, void* implementation, string typeEncoding)
    {
        if (Libobjc.class_addMethod(cls, Libobjc.sel_getUid(selector), implementation, typeEncoding) != 1)
        {
            throw new InvalidOperationException($"Failed to add Objective-C selector: {selector}");
        }
    }
}

internal enum WKNavigationActionPolicy : long
{
    Cancel = 0,
    Allow = 1,
    Download = 2
}

internal enum WKNavigationResponsePolicy : long
{
    Cancel = 0,
    Allow = 1,
    BecomeDownload = 2
}

internal sealed class DecidePolicyForNavigationActionEventArgs : EventArgs
{
    private static readonly IntPtr s_request = Libobjc.sel_getUid("request");
    private static readonly IntPtr s_navigationType = Libobjc.sel_getUid("navigationType");
    private static readonly IntPtr s_targetFrame = Libobjc.sel_getUid("targetFrame");
    private static readonly IntPtr s_isMainFrame = Libobjc.sel_getUid("isMainFrame");

    internal DecidePolicyForNavigationActionEventArgs(IntPtr navigationAction, IntPtr decisionHandler)
    {
        NavigationAction = navigationAction;
        DecisionHandler = decisionHandler;
        Request = NSURLRequest.FromHandle(Libobjc.intptr_objc_msgSend(navigationAction, s_request));
        NavigationType = Libobjc.long_objc_msgSend(navigationAction, s_navigationType);
        TargetFrame = Libobjc.intptr_objc_msgSend(navigationAction, s_targetFrame);
        IsNewWindow = TargetFrame == IntPtr.Zero;
        IsMainFrame = IsNewWindow || Libobjc.int_objc_msgSend(TargetFrame, s_isMainFrame) == 1;
    }

    public IntPtr NavigationAction { get; }

    public NSURLRequest Request { get; }

    public long NavigationType { get; }

    public IntPtr TargetFrame { get; }

    public bool IsMainFrame { get; }

    public bool IsNewWindow { get; }

    public WKNavigationActionPolicy Policy { get; set; } = WKNavigationActionPolicy.Allow;

    private IntPtr DecisionHandler { get; }

    private int _completed;

    internal bool IsDeferred { get; private set; }

    public void DeferCompletion() => IsDeferred = true;

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            InvokeDecisionHandler(DecisionHandler, (long)Policy);
        }
    }

    public void Complete(WKNavigationActionPolicy policy)
    {
        Policy = policy;
        Complete();
    }

    private static unsafe void InvokeDecisionHandler(IntPtr block, long policy)
    {
        var callback = (delegate* unmanaged[Cdecl]<IntPtr, long, void>)BlockLiteral.GetCallback(block);
        callback(block, policy);
    }
}

internal sealed class DecidePolicyForNavigationResponseEventArgs : EventArgs
{
    private static readonly IntPtr s_response = Libobjc.sel_getUid("response");

    internal DecidePolicyForNavigationResponseEventArgs(IntPtr navigationResponse, IntPtr decisionHandler)
    {
        NavigationResponse = navigationResponse;
        DecisionHandler = decisionHandler;
        Response = new NSURLResponse(Libobjc.intptr_objc_msgSend(navigationResponse, s_response), owns: false);
    }

    public IntPtr NavigationResponse { get; }

    public NSURLResponse Response { get; }

    public WKNavigationResponsePolicy Policy { get; set; } = WKNavigationResponsePolicy.Allow;

    private IntPtr DecisionHandler { get; }

    private int _completed;

    internal void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
        {
            InvokeDecisionHandler(DecisionHandler, (long)Policy);
        }
    }

    private static unsafe void InvokeDecisionHandler(IntPtr block, long policy)
    {
        var callback = (delegate* unmanaged[Cdecl]<IntPtr, long, void>)BlockLiteral.GetCallback(block);
        callback(block, policy);
    }
}
