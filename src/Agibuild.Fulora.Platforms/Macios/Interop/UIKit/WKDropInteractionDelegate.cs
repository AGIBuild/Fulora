// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original UIDropInteractionDelegate runtime class.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Agibuild.Fulora;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

namespace Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

internal sealed unsafe class WKDropInteractionDelegate : WkDelegateBase
{
    private const string DispatchLibrary = "/usr/lib/system/libdispatch.dylib";
    private const string FileUrlType = "public.file-url";
    private const string UrlType = "public.url";
    private const string TextType = "public.utf8-plain-text";
    private const string HtmlType = "public.html";

    private static readonly void* s_sessionDidEnter =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&SessionDidEnterCallback;
    private static readonly void* s_sessionDidUpdate =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&SessionDidUpdateCallback;
    private static readonly void* s_sessionDidExit =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&SessionDidExitCallback;
    private static readonly void* s_performDrop =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&PerformDropCallback;
    private static readonly IntPtr s_itemProviderLoadCallback =
        new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&ItemProviderLoadCallback);
    private static readonly IntPtr s_raiseDropCallback =
        new((delegate* unmanaged[Cdecl]<IntPtr, void>)&RaiseDropCallback);

    private static readonly IntPtr s_locationInView = Libobjc.sel_getUid("locationInView:");
    private static readonly IntPtr s_items = Libobjc.sel_getUid("items");
    private static readonly IntPtr s_itemProvider = Libobjc.sel_getUid("itemProvider");
    private static readonly IntPtr s_hasItemConformingToTypeIdentifier =
        Libobjc.sel_getUid("hasItemConformingToTypeIdentifier:");
    private static readonly IntPtr s_loadItemForTypeIdentifier =
        Libobjc.sel_getUid("loadItemForTypeIdentifier:options:completionHandler:");
    private static readonly IntPtr s_absoluteString = Libobjc.sel_getUid("absoluteString");
    private static readonly IntPtr s_path = Libobjc.sel_getUid("path");
    private static readonly IntPtr s_utf8String = Libobjc.sel_getUid("UTF8String");
    private static readonly IntPtr s_class;
    private static readonly ConcurrentDictionary<IntPtr, DropPayloadItemLoadState> s_itemLoadStates = new();
    private static readonly ConcurrentDictionary<IntPtr, GCHandle> s_dropDispatchStates = new();

    [DllImport(DispatchLibrary)]
    private static extern IntPtr dispatch_get_main_queue();

    [DllImport(DispatchLibrary)]
    private static extern void dispatch_async(IntPtr queue, IntPtr block);

    static WKDropInteractionDelegate()
    {
        _ = UIKit.objc_getClass("UIDropInteraction");
        var cls = AllocateClassPair("ManagedFuloraUIDropInteractionDelegate");
        AddProtocol(cls, "UIDropInteractionDelegate");
        AddMethod(cls, "dropInteraction:sessionDidEnter:", s_sessionDidEnter, "v@:@@");
        AddMethod(cls, "dropInteraction:sessionDidUpdate:", s_sessionDidUpdate, "@@:@@");
        AddMethod(cls, "dropInteraction:sessionDidExit:", s_sessionDidExit, "v@:@@");
        AddMethod(cls, "dropInteraction:performDrop:", s_performDrop, "v@:@@");

        if (!RegisterManagedMembers(cls))
        {
            throw new InvalidOperationException("Failed to register managed-self storage for UIDropInteractionDelegate.");
        }

        Libobjc.objc_registerClassPair(cls);
        s_class = cls;
    }

    public WKDropInteractionDelegate() : base(s_class)
    {
    }

    public event EventHandler<DragEventArgs>? DragEntered;

    public event EventHandler<DragEventArgs>? DragUpdated;

    public event EventHandler? DragExited;

    public event EventHandler<DropEventArgs>? DropPerformed;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SessionDidEnterCallback(IntPtr self, IntPtr sel, IntPtr interaction, IntPtr session)
    {
        var managed = ReadManagedSelf<WKDropInteractionDelegate>(self);
        if (managed is null)
        {
            return;
        }

        var (x, y) = GetDropPoint(interaction, session);
        managed.DragEntered?.Invoke(
            managed,
            new DragEventArgs
            {
                Payload = new DragDropPayload(),
                AllowedEffects = DragDropEffects.Copy,
                Effect = DragDropEffects.Copy,
                X = x,
                Y = y
            });
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static IntPtr SessionDidUpdateCallback(IntPtr self, IntPtr sel, IntPtr interaction, IntPtr session)
    {
        var managed = ReadManagedSelf<WKDropInteractionDelegate>(self);
        if (managed is not null)
        {
            var (x, y) = GetDropPoint(interaction, session);
            managed.DragUpdated?.Invoke(
                managed,
                new DragEventArgs
                {
                    Payload = new DragDropPayload(),
                    AllowedEffects = DragDropEffects.Copy,
                    Effect = DragDropEffects.Copy,
                    X = x,
                    Y = y
                });
        }

        var proposal = new UIDropProposal(UIDropOperation.Copy);
        return proposal.Handle;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void SessionDidExitCallback(IntPtr self, IntPtr sel, IntPtr interaction, IntPtr session)
    {
        var managed = ReadManagedSelf<WKDropInteractionDelegate>(self);
        managed?.DragExited?.Invoke(managed, EventArgs.Empty);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void PerformDropCallback(IntPtr self, IntPtr sel, IntPtr interaction, IntPtr session)
    {
        var managed = ReadManagedSelf<WKDropInteractionDelegate>(self);
        if (managed is null)
        {
            return;
        }

        var (x, y) = GetDropPoint(interaction, session);
        var batch = new DropPayloadLoadBatch(managed, x, y);
        batch.Retain();
        ScheduleDropPayloadLoads(session, batch);
        if (!batch.HasPendingLoads)
        {
            batch.Finish();
        }
    }

    private static (double X, double Y) GetDropPoint(IntPtr interaction, IntPtr session)
    {
        var view = Libobjc.intptr_objc_msgSend(interaction, Libobjc.sel_getUid("view"));
        var point = Libobjc.CGPoint_objc_msgSend(session, s_locationInView, view);
        return (point.X, point.Y);
    }

    private static void ScheduleDropPayloadLoads(IntPtr session, DropPayloadLoadBatch batch)
    {
        var itemsHandle = Libobjc.intptr_objc_msgSend(session, s_items);
        if (itemsHandle == IntPtr.Zero)
        {
            return;
        }

        var items = new NSArray(itemsHandle, owns: false);
        var count = items.Count;
        for (nuint index = 0; index < (nuint)count; index++)
        {
            var dragItem = items.ObjectAtIndex(index);
            if (dragItem == IntPtr.Zero)
            {
                continue;
            }

            var itemProvider = Libobjc.intptr_objc_msgSend(dragItem, s_itemProvider);
            if (itemProvider == IntPtr.Zero)
            {
                continue;
            }

            ScheduleLoad(itemProvider, FileUrlType, DropPayloadItemKind.FileUrl, batch);
            ScheduleLoad(itemProvider, UrlType, DropPayloadItemKind.Url, batch);
            ScheduleLoad(itemProvider, TextType, DropPayloadItemKind.Text, batch);
            ScheduleLoad(itemProvider, HtmlType, DropPayloadItemKind.Html, batch);
        }
    }

    private static void ScheduleLoad(
        IntPtr itemProvider,
        string typeIdentifier,
        DropPayloadItemKind kind,
        DropPayloadLoadBatch batch)
    {
        using var type = NSString.Create(typeIdentifier)!;
        if (Libobjc.int_objc_msgSend(itemProvider, s_hasItemConformingToTypeIdentifier, type.Handle) == 0)
        {
            return;
        }

        var itemState = batch.CreateItemState(kind);
        var block = BlockLiteral.GetBlockForFunctionPointer(s_itemProviderLoadCallback, itemState.ToIntPtr());
        itemState.TrackBlock(block);
        Libobjc.void_objc_msgSend(itemProvider, s_loadItemForTypeIdentifier, type.Handle, IntPtr.Zero, block);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void ItemProviderLoadCallback(IntPtr block, IntPtr item, IntPtr error)
    {
        var stateHandle = BlockLiteral.TryGetBlockState(block);
        if (stateHandle != IntPtr.Zero)
        {
            var state = (DropPayloadItemLoadState?)GCHandle.FromIntPtr(stateHandle).Target;
            state?.Complete(item);
            return;
        }

        if (s_itemLoadStates.TryGetValue(block, out var trackedState))
        {
            trackedState.Complete(item);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void RaiseDropCallback(IntPtr block)
    {
        var stateHandle = BlockLiteral.TryGetBlockState(block);
        GCHandle handle;
        if (stateHandle == IntPtr.Zero)
        {
            if (!s_dropDispatchStates.TryRemove(block, out handle))
            {
                return;
            }
        }
        else
        {
            handle = GCHandle.FromIntPtr(stateHandle);
            _ = s_dropDispatchStates.TryRemove(block, out _);
        }

        try
        {
            var state = (DropPayloadDispatchState?)handle.Target;
            state?.Owner.DropPerformed?.Invoke(state.Owner, state.Args);
        }
        finally
        {
            handle.Free();
        }
    }

    private enum DropPayloadItemKind
    {
        FileUrl,
        Url,
        Text,
        Html
    }

    private sealed class DropPayloadItemLoadState(DropPayloadLoadBatch batch, DropPayloadItemKind kind)
    {
        private GCHandle _self;
        private IntPtr _block;
        private int _completed;

        public IntPtr ToIntPtr()
        {
            _self = GCHandle.Alloc(this);
            return GCHandle.ToIntPtr(_self);
        }

        public void TrackBlock(IntPtr block)
        {
            _block = block;
            s_itemLoadStates[block] = this;
        }

        public void Complete(IntPtr item)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            try
            {
                batch.Complete(kind, item);
            }
            finally
            {
                if (_block != IntPtr.Zero)
                {
                    _ = s_itemLoadStates.TryRemove(_block, out _);
                }

                if (_self.IsAllocated)
                {
                    _self.Free();
                }
            }
        }
    }

    private sealed class DropPayloadLoadBatch(WKDropInteractionDelegate owner, double x, double y)
    {
        private readonly object _gate = new();
        private readonly List<FileDropInfo> _files = [];
        private int _pendingLoads;
        private int _finished;
        private GCHandle _self;
        private string? _text;
        private string? _html;
        private string? _uri;

        public bool HasPendingLoads => Volatile.Read(ref _pendingLoads) > 0;

        public void Retain() => _self = GCHandle.Alloc(this);

        public DropPayloadItemLoadState CreateItemState(DropPayloadItemKind kind)
        {
            Interlocked.Increment(ref _pendingLoads);
            return new DropPayloadItemLoadState(this, kind);
        }

        public void Complete(DropPayloadItemKind kind, IntPtr item)
        {
            if (item != IntPtr.Zero)
            {
                lock (_gate)
                {
                    ApplyItem(kind, item);
                }
            }

            if (Interlocked.Decrement(ref _pendingLoads) == 0)
            {
                Finish();
            }
        }

        public void Finish()
        {
            if (Interlocked.Exchange(ref _finished, 1) != 0)
            {
                return;
            }

            DragDropPayload payload;
            lock (_gate)
            {
                payload = new DragDropPayload
                {
                    Files = _files.Count == 0 ? null : _files.ToArray(),
                    Text = _text,
                    Html = _html,
                    Uri = _uri
                };
            }

            var args = new DropEventArgs
            {
                Payload = payload,
                Effect = DragDropEffects.Copy,
                X = x,
                Y = y
            };
            var dispatchStateHandle = GCHandle.Alloc(new DropPayloadDispatchState(owner, args));
            var block = BlockLiteral.GetBlockForFunctionPointer(
                s_raiseDropCallback,
                GCHandle.ToIntPtr(dispatchStateHandle));
            s_dropDispatchStates[block] = dispatchStateHandle;
            dispatch_async(dispatch_get_main_queue(), block);

            if (_self.IsAllocated)
            {
                _self.Free();
            }
        }

        private void ApplyItem(DropPayloadItemKind kind, IntPtr item)
        {
            switch (kind)
            {
                case DropPayloadItemKind.FileUrl:
                    {
                        var path = TryGetUrlPath(item) ?? TryGetString(item);
                        if (!string.IsNullOrEmpty(path))
                        {
                            _files.Add(new FileDropInfo(path));
                        }

                        break;
                    }
                case DropPayloadItemKind.Url:
                    _uri ??= TryGetUrlAbsoluteString(item) ?? TryGetString(item);
                    break;
                case DropPayloadItemKind.Text:
                    _text ??= TryGetString(item);
                    break;
                case DropPayloadItemKind.Html:
                    _html ??= TryGetString(item);
                    break;
            }
        }
    }

    private sealed record DropPayloadDispatchState(WKDropInteractionDelegate Owner, DropEventArgs Args);

    private static string? TryGetUrlAbsoluteString(IntPtr item)
    {
        return item != IntPtr.Zero && NSObject.RespondsToSelector(item, s_absoluteString)
            ? NSString.GetString(Libobjc.intptr_objc_msgSend(item, s_absoluteString))
            : null;
    }

    private static string? TryGetUrlPath(IntPtr item)
    {
        return item != IntPtr.Zero && NSObject.RespondsToSelector(item, s_path)
            ? NSString.GetString(Libobjc.intptr_objc_msgSend(item, s_path))
            : null;
    }

    private static string? TryGetString(IntPtr item)
    {
        return item != IntPtr.Zero && NSObject.RespondsToSelector(item, s_utf8String)
            ? NSString.GetString(item)
            : null;
    }
}
