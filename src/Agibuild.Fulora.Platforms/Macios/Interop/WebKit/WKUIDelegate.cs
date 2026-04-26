// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

/// <remarks>
/// Callers must keep a strong managed reference for as long as the native <c>WKWebView</c>
/// references this delegate; the Objective-C instance stores only a weak managed handle.
/// </remarks>
internal sealed unsafe class WKUIDelegate : WkDelegateBase
{
    private static readonly void* s_requestMediaCapturePermission =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, long, IntPtr, void>)&RequestMediaCapturePermissionCallback;
    private static readonly void* s_runJavaScriptAlertPanel =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&RunJavaScriptAlertPanelCallback;
    private static readonly void* s_runJavaScriptConfirmPanel =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&RunJavaScriptConfirmPanelCallback;
    private static readonly void* s_runJavaScriptTextInputPanel =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&RunJavaScriptTextInputPanelCallback;

    private static readonly IntPtr s_class;

    static WKUIDelegate()
    {
        var cls = AllocateClassPair("ManagedWKUIDelegate");
        AddProtocol(cls, "WKUIDelegate");

        AddMethod(cls, "webView:requestMediaCapturePermissionForOrigin:initiatedByFrame:type:decisionHandler:", s_requestMediaCapturePermission, "v@:@@@q@");
        AddMethod(cls, "webView:runJavaScriptAlertPanelWithMessage:initiatedByFrame:completionHandler:", s_runJavaScriptAlertPanel, "v@:@@@@");
        AddMethod(cls, "webView:runJavaScriptConfirmPanelWithMessage:initiatedByFrame:completionHandler:", s_runJavaScriptConfirmPanel, "v@:@@@@");
        AddMethod(cls, "webView:runJavaScriptTextInputPanelWithPrompt:defaultText:initiatedByFrame:completionHandler:", s_runJavaScriptTextInputPanel, "v@:@@@@@");

        if (!RegisterManagedMembers(cls))
        {
            throw new InvalidOperationException("Failed to register managed-self storage for WKUIDelegate.");
        }

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    public WKUIDelegate() : base(s_class)
    {
    }

    public event EventHandler<MediaCapturePermissionEventArgs>? MediaCapturePermissionRequested;

    public event EventHandler<JavaScriptAlertPanelEventArgs>? JavaScriptAlertPanel;

    public event EventHandler<JavaScriptConfirmPanelEventArgs>? JavaScriptConfirmPanel;

    public event EventHandler<JavaScriptTextInputPanelEventArgs>? JavaScriptTextInputPanel;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RequestMediaCapturePermissionCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr webView,
        IntPtr origin,
        IntPtr frame,
        long type,
        IntPtr decisionHandler)
    {
        var managed = ReadManagedSelf<WKUIDelegate>(self);
        var args = new MediaCapturePermissionEventArgs(origin, frame, type, decisionHandler);
        try
        {
            managed?.MediaCapturePermissionRequested?.Invoke(managed, args);
        }
        catch (Exception ex)
        {
            Debug.Fail(ex.ToString());
            if (!args.HasExplicitDecision)
            {
                args.Decision = WKPermissionDecision.Deny;
            }
        }

        args.Complete();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RunJavaScriptAlertPanelCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr webView,
        IntPtr message,
        IntPtr frame,
        IntPtr completionHandler)
    {
        var managed = ReadManagedSelf<WKUIDelegate>(self);
        var args = new JavaScriptAlertPanelEventArgs(NSString.GetString(message) ?? string.Empty, frame, completionHandler);
        try
        {
            managed?.JavaScriptAlertPanel?.Invoke(managed, args);
        }
        catch (Exception ex)
        {
            Debug.Fail(ex.ToString());
            // Alerts have no decision payload; still complete the native block below.
        }
        finally
        {
            args.Complete();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RunJavaScriptConfirmPanelCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr webView,
        IntPtr message,
        IntPtr frame,
        IntPtr completionHandler)
    {
        var managed = ReadManagedSelf<WKUIDelegate>(self);
        var args = new JavaScriptConfirmPanelEventArgs(NSString.GetString(message) ?? string.Empty, frame, completionHandler);
        try
        {
            managed?.JavaScriptConfirmPanel?.Invoke(managed, args);
        }
        catch (Exception ex)
        {
            Debug.Fail(ex.ToString());
            if (!args.HasExplicitResult)
            {
                args.Decide(false);
            }
        }

        args.Complete();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RunJavaScriptTextInputPanelCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr webView,
        IntPtr prompt,
        IntPtr defaultText,
        IntPtr frame,
        IntPtr completionHandler)
    {
        var managed = ReadManagedSelf<WKUIDelegate>(self);
        var args = new JavaScriptTextInputPanelEventArgs(
            NSString.GetString(prompt) ?? string.Empty,
            NSString.GetString(defaultText),
            frame,
            completionHandler);
        try
        {
            managed?.JavaScriptTextInputPanel?.Invoke(managed, args);
        }
        catch (Exception ex)
        {
            Debug.Fail(ex.ToString());
            if (!args.HasExplicitResult)
            {
                args.Decide(null);
            }
        }

        args.Complete();
    }
}

internal enum WKPermissionDecision : long
{
    Prompt = 0,
    Grant = 1,
    Deny = 2
}

internal sealed class MediaCapturePermissionEventArgs(IntPtr origin, IntPtr frame, long mediaCaptureType, IntPtr decisionHandler) : EventArgs
{
    private WKPermissionDecision _decision = WKPermissionDecision.Prompt;

    public IntPtr Origin { get; } = origin;

    public IntPtr Frame { get; } = frame;

    public long MediaCaptureType { get; } = mediaCaptureType;

    public WKPermissionDecision Decision
    {
        get => _decision;
        set
        {
            _decision = value;
            HasExplicitDecision = true;
        }
    }

    internal bool HasExplicitDecision { get; private set; }

    internal void Complete() => InvokeDecisionHandler(decisionHandler, (long)Decision);

    private static unsafe void InvokeDecisionHandler(IntPtr block, long decision)
    {
        var callback = (delegate* unmanaged[Cdecl]<IntPtr, long, void>)BlockLiteral.GetCallback(block);
        callback(block, decision);
    }
}

internal sealed class JavaScriptAlertPanelEventArgs(string message, IntPtr frame, IntPtr completionHandler) : EventArgs
{
    public string Message { get; } = message;

    public IntPtr Frame { get; } = frame;

    internal void Complete()
    {
        unsafe
        {
            var callback = (delegate* unmanaged[Cdecl]<IntPtr, void>)BlockLiteral.GetCallback(completionHandler);
            callback(completionHandler);
        }
    }
}

internal sealed class JavaScriptConfirmPanelEventArgs(string message, IntPtr frame, IntPtr completionHandler) : EventArgs
{
    private bool _result;

    public string Message { get; } = message;

    public IntPtr Frame { get; } = frame;

    internal bool HasExplicitResult { get; private set; }

    public void Decide(bool result)
    {
        _result = result;
        HasExplicitResult = true;
    }

    internal void Complete()
    {
        unsafe
        {
            var callback = (delegate* unmanaged[Cdecl]<IntPtr, byte, void>)BlockLiteral.GetCallback(completionHandler);
            callback(completionHandler, _result ? (byte)1 : (byte)0);
        }
    }
}

internal sealed class JavaScriptTextInputPanelEventArgs(string prompt, string? defaultText, IntPtr frame, IntPtr completionHandler) : EventArgs
{
    private string? _result = defaultText;

    public string Prompt { get; } = prompt;

    public string? DefaultText { get; } = defaultText;

    public IntPtr Frame { get; } = frame;

    internal bool HasExplicitResult { get; private set; }

    public void Decide(string? result)
    {
        _result = result;
        HasExplicitResult = true;
    }

    internal void Complete()
    {
        using var result = _result is null ? null : NSString.Create(_result);
        unsafe
        {
            var callback = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)BlockLiteral.GetCallback(completionHandler);
            callback(completionHandler, result?.Handle ?? IntPtr.Zero);
        }
    }
}
