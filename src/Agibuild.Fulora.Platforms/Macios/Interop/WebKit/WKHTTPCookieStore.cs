// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original (Task 9; block trampolines mirror WKWebView.EvaluateJavaScriptAsync).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Agibuild.Fulora.Platforms.Macios.Interop;
using Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKHTTPCookieStore : NSObject
{
    private static readonly IntPtr s_objectAtIndex = Libobjc.sel_getUid("objectAtIndex:");
    private static readonly IntPtr s_getAllCookies = Libobjc.sel_getUid("getAllCookies:");
    private static readonly IntPtr s_setCookie = Libobjc.sel_getUid("setCookie:completionHandler:");
    private static readonly IntPtr s_deleteCookie = Libobjc.sel_getUid("deleteCookie:completionHandler:");
    private static readonly unsafe IntPtr s_getAllCookiesTrampoline = new((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&OnAllCookies);
    private static readonly unsafe IntPtr s_voidCompletionTrampoline = new((delegate* unmanaged[Cdecl]<IntPtr, void>)&OnVoidCompletion);

    public WKHTTPCookieStore(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public async Task<IReadOnlyList<NSHTTPCookie>> GetAllCookiesAsync()
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<NSHTTPCookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new CookiesState(tcs);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            var block = BlockLiteral.GetBlockForFunctionPointer(s_getAllCookiesTrampoline, GCHandle.ToIntPtr(stateHandle));
            Libobjc.void_objc_msgSend(Handle, s_getAllCookies, block);
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

    public async Task SetCookieAsync(NSHTTPCookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new VoidCompletionState(tcs);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            var block = BlockLiteral.GetBlockForFunctionPointer(s_voidCompletionTrampoline, GCHandle.ToIntPtr(stateHandle));
            Libobjc.void_objc_msgSend(Handle, s_setCookie, cookie.Handle, block);
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }
        }
    }

    public async Task DeleteCookieAsync(NSHTTPCookie cookie)
    {
        ArgumentNullException.ThrowIfNull(cookie);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new VoidCompletionState(tcs);
        var stateHandle = GCHandle.Alloc(state);
        try
        {
            var block = BlockLiteral.GetBlockForFunctionPointer(s_voidCompletionTrampoline, GCHandle.ToIntPtr(stateHandle));
            Libobjc.void_objc_msgSend(Handle, s_deleteCookie, cookie.Handle, block);
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            if (stateHandle.IsAllocated)
            {
                stateHandle.Free();
            }
        }
    }

    private abstract record CookieStoreCallbackState
    {
        public abstract void TrySetException(Exception exception);
    }

    private sealed record CookiesState(TaskCompletionSource<IReadOnlyList<NSHTTPCookie>> Tcs) : CookieStoreCallbackState
    {
        public override void TrySetException(Exception exception) => _ = Tcs.TrySetException(exception);
    }

    private sealed record VoidCompletionState(TaskCompletionSource Tcs) : CookieStoreCallbackState
    {
        public override void TrySetException(Exception exception) => _ = Tcs.TrySetException(exception);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnAllCookies(IntPtr block, IntPtr arrayHandle)
    {
        if (!TryGetExpectedState<CookiesState>(block, out var state))
        {
            return;
        }

        try
        {
            if (arrayHandle == IntPtr.Zero)
            {
                _ = state.Tcs.TrySetResult([]);
                return;
            }

            var array = new NSArray(arrayHandle, owns: false);
            var n = (int)array.Count;
            if (n == 0)
            {
                _ = state.Tcs.TrySetResult([]);
                return;
            }

            var list = new List<NSHTTPCookie>(n);
            for (var i = 0; i < n; i++)
            {
                var cookieHandle = Libobjc.intptr_objc_msgSend(array.Handle, s_objectAtIndex, (nuint)i);
                if (cookieHandle != IntPtr.Zero)
                {
                    // Retain: NSArray elements are only guaranteed alive for the callback; continuations may run later.
                    list.Add(new NSHTTPCookie(cookieHandle, owns: true));
                }
            }

            _ = state.Tcs.TrySetResult(list);
        }
        catch (Exception ex)
        {
            _ = state.Tcs.TrySetException(ex);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnVoidCompletion(IntPtr block)
    {
        if (!TryGetExpectedState<VoidCompletionState>(block, out var state))
        {
            return;
        }

        _ = state.Tcs.TrySetResult();
    }

    private static bool TryGetExpectedState<T>(IntPtr block, out T state)
        where T : CookieStoreCallbackState
    {
        state = null!;

        var statePtr = BlockLiteral.TryGetBlockState(block);
        if (statePtr == IntPtr.Zero)
        {
            Environment.FailFast($"Missing {nameof(WKHTTPCookieStore)} block callback state.");
        }

        var target = GCHandle.FromIntPtr(statePtr).Target;
        if (target is T typedState)
        {
            state = typedState;
            return true;
        }

        var exception = new InvalidOperationException(
            $"Unexpected {nameof(WKHTTPCookieStore)} block callback state: expected {typeof(T).Name}, got {target?.GetType().Name ?? "null"}.");
        if (target is CookieStoreCallbackState callbackState)
        {
            callbackState.TrySetException(exception);
            return false;
        }

        Environment.FailFast(exception.Message);
        return false;
    }
}
