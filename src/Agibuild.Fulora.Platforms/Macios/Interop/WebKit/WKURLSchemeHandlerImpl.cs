// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

/// <remarks>
/// Callers must keep a strong managed reference for as long as a <c>WKWebViewConfiguration</c>
/// references this handler; the Objective-C instance stores only a weak managed handle.
/// </remarks>
internal sealed unsafe class WKURLSchemeHandlerImpl : WkDelegateBase
{
    private static readonly void* s_startTask =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&StartTaskCallback;
    private static readonly void* s_stopTask =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&StopTaskCallback;

    private static readonly IntPtr s_class;

    static WKURLSchemeHandlerImpl()
    {
        var cls = AllocateClassPair("ManagedWKURLSchemeHandler");
        AddProtocol(cls, "WKURLSchemeHandler");
        AddMethod(cls, "webView:startURLSchemeTask:", s_startTask, "v@:@@");
        AddMethod(cls, "webView:stopURLSchemeTask:", s_stopTask, "v@:@@");

        if (!RegisterManagedMembers(cls))
        {
            throw new InvalidOperationException("Failed to register managed-self storage for WKURLSchemeHandlerImpl.");
        }

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    public WKURLSchemeHandlerImpl() : base(s_class)
    {
    }

    public event EventHandler<WKURLSchemeTaskEventArgs>? StartTask;

    public event EventHandler<WKURLSchemeTaskEventArgs>? StopTask;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StartTaskCallback(IntPtr self, IntPtr sel, IntPtr webView, IntPtr task)
    {
        var managed = ReadManagedSelf<WKURLSchemeHandlerImpl>(self);
        managed?.StartTask?.Invoke(managed, new WKURLSchemeTaskEventArgs(new WKURLSchemeTask(task, owns: false)));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StopTaskCallback(IntPtr self, IntPtr sel, IntPtr webView, IntPtr task)
    {
        var managed = ReadManagedSelf<WKURLSchemeHandlerImpl>(self);
        managed?.StopTask?.Invoke(managed, new WKURLSchemeTaskEventArgs(new WKURLSchemeTask(task, owns: false)));
    }
}
