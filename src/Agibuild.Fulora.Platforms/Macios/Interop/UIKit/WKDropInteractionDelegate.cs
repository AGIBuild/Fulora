// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original UIDropInteractionDelegate runtime class.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Agibuild.Fulora;
using Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

namespace Agibuild.Fulora.Platforms.Macios.Interop.UIKit;

internal sealed unsafe class WKDropInteractionDelegate : WkDelegateBase
{
    private static readonly void* s_sessionDidEnter =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&SessionDidEnterCallback;
    private static readonly void* s_sessionDidUpdate =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&SessionDidUpdateCallback;
    private static readonly void* s_sessionDidExit =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&SessionDidExitCallback;
    private static readonly void* s_performDrop =
        (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&PerformDropCallback;

    private static readonly IntPtr s_locationInView = Libobjc.sel_getUid("locationInView:");
    private static readonly IntPtr s_class;

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
        managed.DropPerformed?.Invoke(
            managed,
            new DropEventArgs
            {
                Payload = new DragDropPayload(),
                Effect = DragDropEffects.Copy,
                X = x,
                Y = y
            });
    }

    private static (double X, double Y) GetDropPoint(IntPtr interaction, IntPtr session)
    {
        var view = Libobjc.intptr_objc_msgSend(interaction, Libobjc.sel_getUid("view"));
        var point = Libobjc.CGPoint_objc_msgSend(session, s_locationInView, view);
        return (point.X, point.Y);
    }
}
