// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (selector subset from legacy Apple shim; evaluateJavaScript block pattern from Avalonia WKWebView).

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Agibuild.Fulora;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.AppKit;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;
using Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKWebView : NSManagedObjectBase
{
    private static readonly IntPtr s_class = WKWebKit.objc_getClass("WKWebView");
    private static readonly IntPtr s_alloc = Libobjc.sel_getUid("alloc");
    private static readonly IntPtr s_initWithFrameConfiguration = Libobjc.sel_getUid("initWithFrame:configuration:");
    private static readonly IntPtr s_registerForDraggedTypes = Libobjc.sel_getUid("registerForDraggedTypes:");
    private static readonly IntPtr s_draggingPasteboard = Libobjc.sel_getUid("draggingPasteboard");
    private static readonly IntPtr s_draggingLocation = Libobjc.sel_getUid("draggingLocation");
    private static readonly IntPtr s_convertPointFromView = Libobjc.sel_getUid("convertPoint:fromView:");
    private static readonly IntPtr s_loadHTMLString = Libobjc.sel_getUid("loadHTMLString:baseURL:");
    private static readonly IntPtr s_loadRequest = Libobjc.sel_getUid("loadRequest:");
    private static readonly IntPtr s_url = Libobjc.sel_getUid("URL");
    private static readonly IntPtr s_canGoBack = Libobjc.sel_getUid("canGoBack");
    private static readonly IntPtr s_goBack = Libobjc.sel_getUid("goBack");
    private static readonly IntPtr s_canGoForward = Libobjc.sel_getUid("canGoForward");
    private static readonly IntPtr s_goForward = Libobjc.sel_getUid("goForward");
    private static readonly IntPtr s_reload = Libobjc.sel_getUid("reload");
    private static readonly IntPtr s_stopLoading = Libobjc.sel_getUid("stopLoading");
    private static readonly IntPtr s_evaluateJavaScript = Libobjc.sel_getUid("evaluateJavaScript:completionHandler:");
    private static readonly IntPtr s_configuration = Libobjc.sel_getUid("configuration");
    private static readonly IntPtr s_setCustomUserAgent = Libobjc.sel_getUid("setCustomUserAgent:");
    private static readonly IntPtr s_pageZoom = Libobjc.sel_getUid("pageZoom");
    private static readonly IntPtr s_setPageZoom = Libobjc.sel_getUid("setPageZoom:");
    private static readonly IntPtr s_setInspectable = Libobjc.sel_getUid("setInspectable:");
    private static readonly IntPtr s_setUnderPageBackgroundColor = Libobjc.sel_getUid("setUnderPageBackgroundColor:");
    private static readonly IntPtr s_takeSnapshotWithConfiguration =
        Libobjc.sel_getUid("takeSnapshotWithConfiguration:completionHandler:");
    private static readonly IntPtr s_createPdfWithConfiguration =
        Libobjc.sel_getUid("createPDFWithConfiguration:completionHandler:");
    private static readonly IntPtr s_setNavigationDelegate = Libobjc.sel_getUid("setNavigationDelegate:");
    private static readonly IntPtr s_setUIDelegate = Libobjc.sel_getUid("setUIDelegate:");
    private static readonly IntPtr s_isKindOfClass = Libobjc.sel_getUid("isKindOfClass:");
    private static readonly unsafe IntPtr s_evaluateScriptCallback = new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&EvaluateJavaScriptTrampoline);
    private static readonly unsafe IntPtr s_snapshotCallback = new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&SnapshotTrampoline);
    private static readonly unsafe IntPtr s_pdfCallback = new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&PdfTrampoline);
    private static readonly unsafe void* s_draggingEnteredCallback =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, nuint>)&DraggingEnteredCallback;
    private static readonly unsafe void* s_draggingUpdatedCallback =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, nuint>)&DraggingUpdatedCallback;
    private static readonly unsafe void* s_draggingExitedCallback =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&DraggingExitedCallback;
    private static readonly unsafe void* s_performDragOperationCallback =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte>)&PerformDragOperationCallback;
    private static readonly IntPtr s_managedClass = CreateManagedClass();

    public WKWebView(WKWebViewConfiguration configuration) : base(NewInstance(configuration), owns: true)
    {
        if (OperatingSystem.IsMacOS())
        {
            RegisterForDraggedTypes();
        }
    }

    private static IntPtr NewInstance(WKWebViewConfiguration configuration)
    {
        var allocated = Libobjc.intptr_objc_msgSend(s_managedClass, s_alloc);
        var frame = new CGRect(0, 0, 0, 0);
        return Libobjc.intptr_objc_msgSend(allocated, s_initWithFrameConfiguration, frame, configuration.Handle);
    }

    public event EventHandler<DragEventArgs>? DragEntered;

    public event EventHandler<DragEventArgs>? DragUpdated;

    public event EventHandler? DragExited;

    public event EventHandler<DropEventArgs>? DropPerformed;

    public NSUrl? Url
    {
        get
        {
            var h = Libobjc.intptr_objc_msgSend(Handle, s_url);
            return h == IntPtr.Zero ? null : new NSUrl(h, owns: false);
        }
    }

    public bool CanGoBack => Libobjc.int_objc_msgSend(Handle, s_canGoBack) == 1;

    public bool CanGoForward => Libobjc.int_objc_msgSend(Handle, s_canGoForward) == 1;

    public WKWebViewConfiguration Configuration =>
        new(Libobjc.intptr_objc_msgSend(Handle, s_configuration), owns: false);

    public string? CustomUserAgent
    {
        set
        {
            using var valueString = NSString.Create(value);
            Libobjc.void_objc_msgSend(Handle, s_setCustomUserAgent, valueString?.Handle ?? IntPtr.Zero);
        }
    }

    public double PageZoom
    {
        get => Libobjc.double_objc_msgSend(Handle, s_pageZoom);
        set => Libobjc.void_objc_msgSend(Handle, s_setPageZoom, value);
    }

    public void SetInspectable(bool enabled)
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(13, 3) ||
            OperatingSystem.IsIOSVersionAtLeast(16, 4))
        {
            Libobjc.void_objc_msgSend(Handle, s_setInspectable, enabled ? 1 : 0);
        }
    }

    public void SetDrawsBackground(bool enabled)
    {
        using var key = NSString.Create("drawsBackground")!;
        SetValueForKey((enabled ? NSNumber.Yes : NSNumber.No).Handle, key);
    }

    public void SetUnderPageBackgroundColor(NSColor color)
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(12))
        {
            Libobjc.void_objc_msgSend(Handle, s_setUnderPageBackgroundColor, color.Handle);
        }
    }

    public WKNavigationDelegate? NavigationDelegate
    {
        set => Libobjc.void_objc_msgSend(Handle, s_setNavigationDelegate, value?.Handle ?? IntPtr.Zero);
    }

    public WKUIDelegate? UIDelegate
    {
        set => Libobjc.void_objc_msgSend(Handle, s_setUIDelegate, value?.Handle ?? IntPtr.Zero);
    }

    private void RegisterForDraggedTypes()
    {
        using var fileUrl = NSString.Create("public.file-url");
        using var text = NSString.Create("public.utf8-plain-text");
        using var legacyText = NSString.Create("NSStringPboardType");
        using var html = NSString.Create("public.html");
        using var url = NSString.Create("public.url");
        using var types = NSArray.FromHandles(
            fileUrl.Handle,
            text.Handle,
            legacyText.Handle,
            html.Handle,
            url.Handle);
        Libobjc.void_objc_msgSend(Handle, s_registerForDraggedTypes, types.Handle);
    }

    public void GoBack() => _ = Libobjc.intptr_objc_msgSend(Handle, s_goBack);

    public void GoForward() => _ = Libobjc.intptr_objc_msgSend(Handle, s_goForward);

    public void Reload() => _ = Libobjc.intptr_objc_msgSend(Handle, s_reload);

    public void Stop() => Libobjc.void_objc_msgSend(Handle, s_stopLoading);

    public void LoadHTMLString(string html, NSUrl? baseUrl)
    {
        ArgumentNullException.ThrowIfNull(html);
        using var htmlNs = NSString.Create(html)!;
        Libobjc.void_objc_msgSend(Handle, s_loadHTMLString, htmlNs.Handle, baseUrl?.Handle ?? IntPtr.Zero);
    }

    public void Load(NSURLRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = Libobjc.intptr_objc_msgSend(Handle, s_loadRequest, request.Handle);
    }

    public async Task<NSObject?> EvaluateJavaScriptAsync(string script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var tcs = new TaskCompletionSource<NSObject?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new JSEvalState(tcs);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            using var scriptStr = NSString.Create(script)!;
            var block = BlockLiteral.GetBlockForFunctionPointer(s_evaluateScriptCallback, GCHandle.ToIntPtr(stateHandle));
            Libobjc.void_objc_msgSend(Handle, s_evaluateJavaScript, scriptStr.Handle, block);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }
        }
    }

    public async Task<byte[]> CaptureScreenshotPngAsync()
    {
        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new BytesCompletionState(tcs);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            using var config = new WKSnapshotConfiguration();
            var block = BlockLiteral.GetBlockForFunctionPointer(s_snapshotCallback, GCHandle.ToIntPtr(stateHandle));
            Libobjc.void_objc_msgSend(Handle, s_takeSnapshotWithConfiguration, config.Handle, block);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }
        }
    }

    public async Task<byte[]> CreatePdfAsync()
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(11, 3) &&
            !OperatingSystem.IsIOSVersionAtLeast(14))
        {
            throw new PlatformNotSupportedException("WKWebView PDF export requires macOS 11.3 or iOS 14.0 or later.");
        }

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new BytesCompletionState(tcs);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            var block = BlockLiteral.GetBlockForFunctionPointer(s_pdfCallback, GCHandle.ToIntPtr(stateHandle));
            Libobjc.void_objc_msgSend(Handle, s_createPdfWithConfiguration, IntPtr.Zero, block);
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }
        }
    }

    private sealed record JSEvalState(TaskCompletionSource<NSObject?> Tcs);

    private sealed record BytesCompletionState(TaskCompletionSource<byte[]> Tcs);

    private static unsafe IntPtr CreateManagedClass()
    {
        var cls = Libobjc.objc_allocateClassPair(s_class, "ManagedFuloraWKWebView", 0);
        if (cls == IntPtr.Zero)
        {
            return WKWebKit.objc_getClass("ManagedFuloraWKWebView");
        }

        if (OperatingSystem.IsMacOS())
        {
            var protocol = Libobjc.objc_getProtocol("NSDraggingDestination");
            if (protocol != IntPtr.Zero)
            {
                _ = Libobjc.class_addProtocol(cls, protocol);
            }

            AddMethod(cls, "draggingEntered:", s_draggingEnteredCallback, "Q@:@");
            AddMethod(cls, "draggingUpdated:", s_draggingUpdatedCallback, "Q@:@");
            AddMethod(cls, "draggingExited:", s_draggingExitedCallback, "v@:@");
            AddMethod(cls, "performDragOperation:", s_performDragOperationCallback, "B@:@");
        }

        if (!RegisterManagedMembers(cls))
        {
            throw new InvalidOperationException("Failed to register managed-self storage for WKWebView.");
        }

        Libobjc.objc_registerClassPair(cls);
        return cls;
    }

    private static unsafe void AddMethod(IntPtr cls, string selector, void* implementation, string typeEncoding)
    {
        if (Libobjc.class_addMethod(cls, Libobjc.sel_getUid(selector), implementation, typeEncoding) != 1)
        {
            throw new InvalidOperationException($"Failed to add Objective-C selector: {selector}");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint DraggingEnteredCallback(IntPtr self, IntPtr sel, IntPtr draggingInfo)
    {
        var managed = ReadManagedSelf<WKWebView>(self);
        if (managed is null)
        {
            return ToNSDragOperation(DragDropEffects.Copy);
        }

        var (x, y) = GetDragPoint(self, draggingInfo);
        var args = new DragEventArgs
        {
            Payload = CreateDragPayload(draggingInfo),
            AllowedEffects = DragDropEffects.Copy,
            Effect = DragDropEffects.Copy,
            X = x,
            Y = y
        };
        managed.DragEntered?.Invoke(managed, args);
        return ToNSDragOperation(args.Effect);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nuint DraggingUpdatedCallback(IntPtr self, IntPtr sel, IntPtr draggingInfo)
    {
        var managed = ReadManagedSelf<WKWebView>(self);
        if (managed is null)
        {
            return ToNSDragOperation(DragDropEffects.Copy);
        }

        var (x, y) = GetDragPoint(self, draggingInfo);
        var args = new DragEventArgs
        {
            Payload = new DragDropPayload(),
            AllowedEffects = DragDropEffects.Copy,
            Effect = DragDropEffects.Copy,
            X = x,
            Y = y
        };
        managed.DragUpdated?.Invoke(managed, args);
        return ToNSDragOperation(args.Effect);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DraggingExitedCallback(IntPtr self, IntPtr sel, IntPtr draggingInfo)
    {
        var managed = ReadManagedSelf<WKWebView>(self);
        managed?.DragExited?.Invoke(managed, EventArgs.Empty);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte PerformDragOperationCallback(IntPtr self, IntPtr sel, IntPtr draggingInfo)
    {
        var managed = ReadManagedSelf<WKWebView>(self);
        if (managed is null)
        {
            return 0;
        }

        var (x, y) = GetDragPoint(self, draggingInfo);
        managed.DropPerformed?.Invoke(
            managed,
            new DropEventArgs
            {
                Payload = CreateDragPayload(draggingInfo),
                Effect = DragDropEffects.Copy,
                X = x,
                Y = y
            });
        return 1;
    }

    private static (double X, double Y) GetDragPoint(IntPtr webView, IntPtr draggingInfo)
    {
        var windowPoint = Libobjc.CGPoint_objc_msgSend(draggingInfo, s_draggingLocation);
        var viewPoint = Libobjc.CGPoint_objc_msgSend(webView, s_convertPointFromView, windowPoint, IntPtr.Zero);
        return (viewPoint.X, viewPoint.Y);
    }

    private static DragDropPayload CreateDragPayload(IntPtr draggingInfo)
    {
        var pasteboardHandle = Libobjc.intptr_objc_msgSend(draggingInfo, s_draggingPasteboard);
        if (pasteboardHandle == IntPtr.Zero)
        {
            return new DragDropPayload();
        }

        var pasteboard = new NSPasteboard(pasteboardHandle, owns: false);
        var files = new List<FileDropInfo>();
        string? uri = pasteboard.PasteboardUri;
        foreach (var url in pasteboard.ReadUrls())
        {
            if (url.IsFileUrl)
            {
                files.Add(CreateFileDropInfo(url));
            }
            else
            {
                uri ??= url.AbsoluteString;
            }
        }

        return new DragDropPayload
        {
            Files = files.Count == 0 ? null : files,
            Text = pasteboard.Text,
            Html = pasteboard.Html,
            Uri = uri
        };
    }

    private static FileDropInfo CreateFileDropInfo(NSUrl url)
    {
        var path = url.Path ?? string.Empty;
        long? size = null;
        if (File.Exists(path))
        {
            size = new FileInfo(path).Length;
        }

        return new FileDropInfo(path, MimeType: null, Size: size);
    }

    private static nuint ToNSDragOperation(DragDropEffects effect)
    {
        nuint operation = 0;
        if ((effect & DragDropEffects.Copy) != 0)
        {
            operation |= 1;
        }

        if ((effect & DragDropEffects.Link) != 0)
        {
            operation |= 2;
        }

        if ((effect & DragDropEffects.Move) != 0)
        {
            operation |= 16;
        }

        return operation;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void EvaluateJavaScriptTrampoline(IntPtr block, IntPtr value, IntPtr nsError)
    {
        var statePtr = BlockLiteral.TryGetBlockState(block);
        if (statePtr == IntPtr.Zero)
        {
            return;
        }

        if (GCHandle.FromIntPtr(statePtr).Target is not JSEvalState state)
        {
            return;
        }

        if (nsError != IntPtr.Zero)
        {
            _ = state.Tcs.TrySetException(NSError.ToException(nsError));
            return;
        }

        _ = state.Tcs.TrySetResult(WrapJsResult(value));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SnapshotTrampoline(IntPtr block, IntPtr imageHandle, IntPtr nsError)
    {
        var state = TryGetBytesCompletionState(block);
        if (state is null)
        {
            return;
        }

        if (nsError != IntPtr.Zero)
        {
            _ = state.Tcs.TrySetException(NSError.ToException(nsError));
            return;
        }

        if (imageHandle == IntPtr.Zero)
        {
            _ = state.Tcs.TrySetException(new InvalidOperationException("Screenshot capture failed."));
            return;
        }

        var png = OperatingSystem.IsIOS()
            ? new UIImage(imageHandle, owns: false).ToPng()
            : ToMacPng(imageHandle);
        if (png is null)
        {
            _ = state.Tcs.TrySetException(new InvalidOperationException("Screenshot PNG conversion failed."));
            return;
        }

        _ = state.Tcs.TrySetResult(png.ToArray());
    }

    private static NSData? ToMacPng(IntPtr imageHandle)
    {
        var image = new NSImage(imageHandle, owns: false);
        var tiff = image.TiffRepresentation;
        if (tiff is null)
        {
            return null;
        }

        using var bitmap = NSBitmapImageRep.FromTiff(tiff);
        return bitmap?.ToPng();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PdfTrampoline(IntPtr block, IntPtr dataHandle, IntPtr nsError)
    {
        var state = TryGetBytesCompletionState(block);
        if (state is null)
        {
            return;
        }

        if (nsError != IntPtr.Zero)
        {
            _ = state.Tcs.TrySetException(NSError.ToException(nsError));
            return;
        }

        if (dataHandle == IntPtr.Zero)
        {
            _ = state.Tcs.TrySetException(new InvalidOperationException("PDF printing failed."));
            return;
        }

        var data = new NSData(dataHandle, owns: false);
        _ = state.Tcs.TrySetResult(data.ToArray());
    }

    private static BytesCompletionState? TryGetBytesCompletionState(IntPtr block)
    {
        var statePtr = BlockLiteral.TryGetBlockState(block);
        if (statePtr == IntPtr.Zero)
        {
            return null;
        }

        return GCHandle.FromIntPtr(statePtr).Target as BytesCompletionState;
    }

    private static NSObject? WrapJsResult(IntPtr value)
    {
        if (value == IntPtr.Zero || IsNSNull(value))
        {
            return null;
        }

        if (NSString.TryGetString(value) is not null)
        {
            return NSString.FromHandle(value);
        }

        return new ObjCId(value, owns: false);
    }

    private static bool IsNSNull(IntPtr value)
    {
        var nsNullClass = Libobjc.objc_getClass("NSNull");
        return Libobjc.int_objc_msgSend(value, s_isKindOfClass, nsNullClass) == 1;
    }

    private sealed class ObjCId : NSObject
    {
        public ObjCId(IntPtr handle, bool owns) : base(handle, owns)
        {
        }
    }
}
