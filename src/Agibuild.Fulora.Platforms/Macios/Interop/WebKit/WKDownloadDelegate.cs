// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

/// <remarks>
/// Callers must keep a strong managed reference for as long as WebKit references this delegate;
/// the Objective-C instance stores only a weak managed handle.
/// </remarks>
internal sealed unsafe class WKDownloadDelegate : WkDelegateBase
{
    private static readonly void* s_decideDestination =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DecideDestinationCallback;
    private static readonly void* s_didFail =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, void>)&DidFailCallback;
    private static readonly void* s_didFinish =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&DidFinishCallback;

    private static readonly IntPtr s_class;

    static WKDownloadDelegate()
    {
        if (!IsSupported)
        {
            return;
        }

        var cls = AllocateClassPair("ManagedWKDownloadDelegate");
        // Current macOS WebKit exposes WKDownload callbacks but does not publish a
        // WKDownloadDelegate protocol through objc_getProtocol. Register selectors directly.
        AddMethod(
            cls,
            "download:decideDestinationUsingResponse:suggestedFilename:completionHandler:",
            s_decideDestination,
            "v@:@@@@");
        AddMethod(cls, "download:didFailWithError:resumeData:", s_didFail, "v@:@@@");
        AddMethod(cls, "downloadDidFinish:", s_didFinish, "v@:@");

        if (!RegisterManagedMembers(cls))
        {
            throw new InvalidOperationException("Failed to register managed-self storage for WKDownloadDelegate.");
        }

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    public WKDownloadDelegate() : base(GetSupportedClass())
    {
    }

    private static bool IsSupported =>
        OperatingSystem.IsMacOSVersionAtLeast(11, 3) || OperatingSystem.IsIOSVersionAtLeast(14, 5);

    private static IntPtr GetSupportedClass()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("WKDownloadDelegate requires macOS 11.3+ or iOS 14.5+.");
        }

        return s_class;
    }

    public event EventHandler<WKDownloadDestinationEventArgs>? DecideDestination;

    public event EventHandler<WKDownloadFailedEventArgs>? DidFail;

    public event EventHandler<WKDownloadEventArgs>? DidFinish;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DecideDestinationCallback(
        IntPtr self,
        IntPtr sel,
        IntPtr download,
        IntPtr response,
        IntPtr suggestedFilename,
        IntPtr completionHandler)
    {
        var managed = ReadManagedSelf<WKDownloadDelegate>(self);
        var args = new WKDownloadDestinationEventArgs(
            new WKDownload(download, owns: false),
            new NSURLResponse(response, owns: false),
            NSString.GetString(suggestedFilename) ?? string.Empty,
            completionHandler);
        try
        {
            managed?.DecideDestination?.Invoke(managed, args);
        }
        catch
        {
            if (!args.HasExplicitDestination)
            {
                args.Decide(null);
            }
        }

        args.Complete();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidFailCallback(IntPtr self, IntPtr sel, IntPtr download, IntPtr error, IntPtr resumeData)
    {
        var managed = ReadManagedSelf<WKDownloadDelegate>(self);
        managed?.DidFail?.Invoke(
            managed,
            new WKDownloadFailedEventArgs(new WKDownload(download, owns: false), new NSError(error), resumeData));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DidFinishCallback(IntPtr self, IntPtr sel, IntPtr download)
    {
        var managed = ReadManagedSelf<WKDownloadDelegate>(self);
        managed?.DidFinish?.Invoke(managed, new WKDownloadEventArgs(new WKDownload(download, owns: false)));
    }
}

internal class WKDownloadEventArgs(WKDownload download) : EventArgs
{
    public WKDownload Download { get; } = download;
}

internal sealed class WKDownloadFailedEventArgs(WKDownload download, NSError error, IntPtr resumeData)
    : WKDownloadEventArgs(download)
{
    public NSError Error { get; } = error;

    public IntPtr ResumeData { get; } = resumeData;
}

internal sealed class WKDownloadDestinationEventArgs(
    WKDownload download,
    NSURLResponse response,
    string suggestedFilename,
    IntPtr completionHandler) : WKDownloadEventArgs(download)
{
    private NSUrl? _destination;

    public NSURLResponse Response { get; } = response;

    public string SuggestedFilename { get; } = suggestedFilename;

    internal bool HasExplicitDestination { get; private set; }

    public void Decide(NSUrl? destination)
    {
        _destination = destination;
        HasExplicitDestination = true;
    }

    internal void Complete()
    {
        unsafe
        {
            var callback = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)BlockLiteral.GetCallback(completionHandler);
            callback(completionHandler, _destination?.Handle ?? IntPtr.Zero);
        }
    }
}
