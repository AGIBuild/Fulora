// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;
using Agibuild.Fulora.Platforms.Macios.Interop.Security;
using MacSecurity = Agibuild.Fulora.Platforms.Macios.Interop.Security.Security;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

/// <remarks>
/// Callers must keep a strong managed reference for as long as the native <c>WKWebView</c>
/// references this delegate; the Objective-C instance stores only a weak managed handle.
/// </remarks>
internal sealed unsafe class WKNavigationDelegate : WkDelegateBase
{
    private static readonly void* s_didFinish =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidFinishNavigationCallback;
    private static readonly void* s_decidePolicyForNavigationAction =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DecidePolicyForNavigationActionCallback;
    private static readonly void* s_didFailProvisional =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidFailProvisionalCallback;
    private static readonly void* s_didFail =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidFailCallback;
    private static readonly void* s_decidePolicyForNavigationResponse =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DecidePolicyForNavigationResponseCallback;
    private static readonly void* s_didReceiveChallenge =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidReceiveAuthenticationChallengeCallback;

    private static readonly IntPtr s_protectionSpace = Libobjc.sel_getUid("protectionSpace");
    private static readonly IntPtr s_serverTrust = Libobjc.sel_getUid("serverTrust");
    private static readonly IntPtr s_host = Libobjc.sel_getUid("host");

    private static readonly IntPtr s_class;

    static WKNavigationDelegate()
    {
        var cls = AllocateClassPair("ManagedWKNavigationDelegate");
        AddProtocol(cls, "WKNavigationDelegate");

        AddMethod(cls, "webView:didFinishNavigation:", s_didFinish, "v@:@@");
        AddMethod(cls, "webView:decidePolicyForNavigationAction:decisionHandler:", s_decidePolicyForNavigationAction, "v@:@@@");
        AddMethod(cls, "webView:didFailProvisionalNavigation:withError:", s_didFailProvisional, "v@:@@@");
        AddMethod(cls, "webView:didFailNavigation:withError:", s_didFail, "v@:@@@");
        AddMethod(cls, "webView:decidePolicyForNavigationResponse:decisionHandler:", s_decidePolicyForNavigationResponse, "v@:@@@");
        AddMethod(cls, "webView:didReceiveAuthenticationChallenge:completionHandler:", s_didReceiveChallenge, "v@:@@@");

        if (!RegisterManagedMembers(cls))
        {
            throw new InvalidOperationException("Failed to register managed-self storage for WKNavigationDelegate.");
        }

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    public WKNavigationDelegate() : base(s_class)
    {
    }

    public event EventHandler? DidFinishNavigation;

    public event EventHandler<NSError>? DidFailProvisionalNavigation;

    public event EventHandler<NSError>? DidFailNavigation;

    public event EventHandler<DecidePolicyForNavigationActionEventArgs>? DecidePolicyForNavigationAction;

    public event EventHandler<DecidePolicyForNavigationResponseEventArgs>? DecidePolicyForNavigationResponse;

    public event EventHandler<ServerTrustChallengeEventArgs>? DidReceiveServerTrustChallenge;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidFinishNavigationCallback(IntPtr self, IntPtr sel, IntPtr webView, IntPtr navigation)
    {
        var managed = ReadManagedSelf<WKNavigationDelegate>(self);
        managed?.DidFinishNavigation?.Invoke(managed, EventArgs.Empty);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DecidePolicyForNavigationActionCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr webView,
        IntPtr navigationAction,
        IntPtr decisionHandler)
    {
        var managed = ReadManagedSelf<WKNavigationDelegate>(self);
        var args = new DecidePolicyForNavigationActionEventArgs(navigationAction, decisionHandler);
        try
        {
            managed?.DecidePolicyForNavigationAction?.Invoke(managed, args);
        }
        catch
        {
            if (!args.IsDeferred)
            {
                args.Policy = WKNavigationActionPolicy.Cancel;
            }
        }

        if (!args.IsDeferred)
        {
            args.Complete();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidFailProvisionalCallback(IntPtr self, IntPtr sel, IntPtr webView, IntPtr navigation, IntPtr error)
    {
        var managed = ReadManagedSelf<WKNavigationDelegate>(self);
        managed?.DidFailProvisionalNavigation?.Invoke(managed, new NSError(error));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidFailCallback(IntPtr self, IntPtr sel, IntPtr webView, IntPtr navigation, IntPtr error)
    {
        var managed = ReadManagedSelf<WKNavigationDelegate>(self);
        managed?.DidFailNavigation?.Invoke(managed, new NSError(error));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DecidePolicyForNavigationResponseCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr webView,
        IntPtr navigationResponse,
        IntPtr decisionHandler)
    {
        var managed = ReadManagedSelf<WKNavigationDelegate>(self);
        var args = new DecidePolicyForNavigationResponseEventArgs(navigationResponse, decisionHandler);
        try
        {
            managed?.DecidePolicyForNavigationResponse?.Invoke(managed, args);
        }
        catch
        {
            args.Policy = WKNavigationResponsePolicy.Cancel;
        }

        args.Complete();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidReceiveAuthenticationChallengeCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr webView,
        IntPtr challenge,
        IntPtr completionHandler)
    {
        var managed = ReadManagedSelf<WKNavigationDelegate>(self);
        if (managed?.DidReceiveServerTrustChallenge is null)
        {
            InvokeAuthenticationChallengeCompletion(completionHandler, NSURLSessionAuthChallengeDisposition.PerformDefaultHandling);
            return;
        }

        var args = BuildServerTrustEventArgs(challenge);
        if (args is null)
        {
            InvokeAuthenticationChallengeCompletion(completionHandler, NSURLSessionAuthChallengeDisposition.PerformDefaultHandling);
            return;
        }

        try
        {
            managed.DidReceiveServerTrustChallenge.Invoke(managed, args);
        }
        finally
        {
            InvokeAuthenticationChallengeCompletion(completionHandler, NSURLSessionAuthChallengeDisposition.CancelAuthenticationChallenge);
        }
    }

    private static ServerTrustChallengeEventArgs? BuildServerTrustEventArgs(IntPtr challenge)
    {
        var protectionSpace = Libobjc.intptr_objc_msgSend(challenge, s_protectionSpace);
        if (protectionSpace == IntPtr.Zero)
        {
            return null;
        }

        var trustHandle = Libobjc.intptr_objc_msgSend(protectionSpace, s_serverTrust);
        if (trustHandle == IntPtr.Zero)
        {
            return null;
        }

        var host = NSString.GetString(Libobjc.intptr_objc_msgSend(protectionSpace, s_host)) ?? string.Empty;
        using var trust = new SecTrust(trustHandle, owns: false);
        var isTrusted = trust.Evaluate(out var cfError);
        var platformRawCode = 0;
        var errorSummary = isTrusted ? "ServerTrustChallenge" : "ServerTrustEvaluationFailed";
        if (cfError != IntPtr.Zero)
        {
            try
            {
                platformRawCode = checked((int)MacSecurity.CFErrorGetCode(cfError));
                var description = MacSecurity.CFErrorCopyDescription(cfError);
                if (description != IntPtr.Zero)
                {
                    try
                    {
                        errorSummary = NSString.GetString(description) ?? errorSummary;
                    }
                    finally
                    {
                        MacSecurity.CFRelease(description);
                    }
                }
            }
            finally
            {
                MacSecurity.CFRelease(cfError);
            }
        }

        string? subject = null;
        string? issuer = null;
        DateTimeOffset? validFrom = null;
        DateTimeOffset? validTo = null;
        var chain = trust.CopyCertificateChain();
        try
        {
            if (chain.Count > 0)
            {
                var metadata = X509MetadataExtractor.Extract(chain[0]);
                subject = metadata.Subject;
                issuer = metadata.Issuer;
                validFrom = metadata.NotBefore;
                validTo = metadata.NotAfter;
            }
        }
        finally
        {
            foreach (var certificate in chain)
            {
                certificate.Dispose();
            }
        }

        return new ServerTrustChallengeEventArgs
        {
            Host = host,
            ErrorSummary = errorSummary,
            PlatformRawCode = platformRawCode,
            CertificateSubject = subject,
            CertificateIssuer = issuer,
            ValidFrom = validFrom,
            ValidTo = validTo
        };
    }

    private static unsafe void InvokeAuthenticationChallengeCompletion(
        IntPtr completionHandler,
        NSURLSessionAuthChallengeDisposition disposition)
    {
        var callback = (delegate* unmanaged[Cdecl]<IntPtr, long, IntPtr, void>)BlockLiteral.GetCallback(completionHandler);
        callback(completionHandler, (long)disposition, IntPtr.Zero);
    }
}

internal enum NSURLSessionAuthChallengeDisposition : long
{
    PerformDefaultHandling = 1,
    CancelAuthenticationChallenge = 2
}

internal sealed class ServerTrustChallengeEventArgs : EventArgs
{
    public required string Host { get; init; }

    public required string ErrorSummary { get; init; }

    public required int PlatformRawCode { get; init; }

    public string? CertificateSubject { get; init; }

    public string? CertificateIssuer { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }
}
