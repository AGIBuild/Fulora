// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal class NSDictionary : NSObject
{
    private static readonly IntPtr s_class = Libobjc.objc_getClass("NSDictionary");
    // VENDOR REMOVAL (Fulora @ Avalonia SHA 4e16564): the upstream `s_count` field and the
    // `Count` IntPtr property were dead code with a latent ABI bug. Upstream initialised
    // `s_count = Libobjc.objc_getClass("count")` (a Class*) and then sent it as the SEL arg to
    // `objc_msgSend`, which is undefined behaviour at runtime. The only upstream caller was
    // `WKWebKitNativeHttpRequestHeaders.cs`, which Fulora does NOT vendor (we use the count via
    // `CFDictionaryGetCount` in `AsStringDictionary` instead). Removing both members eliminates
    // a footgun without changing observed behaviour. If a future upstream revision fixes the
    // binding (e.g. switches to `sel_getUid("count")` + `nuint_objc_msgSend`), restore the
    // member at re-vendor time and add a Phase 2 wrapper that uses it.
    private static readonly IntPtr s_dictionaryWithObjects = Libobjc.sel_getUid("dictionaryWithObjects:forKeys:count:");
    private static readonly IntPtr s_dictionary = Libobjc.sel_getUid("dictionary");
    private static readonly nint s_ObjectForKey = Libobjc.sel_getUid("objectForKey:");

    private NSDictionary(IntPtr handle, bool owns) : base(handle, owns)
    {
    }

    public static NSDictionary FromHandle(IntPtr handle)
    {
        return new NSDictionary(handle, false);
    }

    public static NSDictionary Empty => new(Libobjc.intptr_objc_msgSend(s_class, s_dictionary), owns: true);

    public nint ObjectForKey(nint key)
    {
        return Libobjc.intptr_objc_msgSend(Handle, s_ObjectForKey, key);
    }

    public static unsafe NSDictionary WithObjects(
        IReadOnlyList<NSObject> objects,
        IReadOnlyList<NSObject> keys,
        uint count)
    {
        var objPtrs = stackalloc IntPtr[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            objPtrs[i] = objects[i].Handle;
        }
        var keyPtrs = stackalloc IntPtr[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            keyPtrs[i] = keys[i].Handle;
        }

        // Pass count as UIntPtr, it is expected as NSUInteger:
        // When building 32-bit applications, NSUInteger is a 32-bit unsigned integer. A 64-bit application treats NSUInteger as a 64-bit unsigned integer
        var handle = Libobjc.intptr_objc_msgSend(s_class, s_dictionaryWithObjects, new IntPtr(objPtrs), new IntPtr(keyPtrs), new UIntPtr(count));
        return new NSDictionary(handle, true);
    }

    public static unsafe NSDictionary WithObjects(
        IntPtr[] objects,
        IntPtr[] keys,
        uint count)
    {
        fixed (void* objPtrs = objects)
        fixed (void* keyPtrs = keys)
        {
            var handle = Libobjc.intptr_objc_msgSend(s_class, s_dictionaryWithObjects, new IntPtr(objPtrs),
                new IntPtr(keyPtrs), (int)count);
            return new NSDictionary(handle, true);
        }
    }

    public static unsafe Dictionary<string, object?> AsStringDictionary(IntPtr handle)
    {
        var dictionary = new Dictionary<string, object?>();

        if (handle != default
            && CFDictionaryGetCount(handle) is var count and > 0)
        {
            var keys = new IntPtr[count];
            var values = new IntPtr[count];
            fixed (IntPtr* keysPtr = keys)
            fixed (IntPtr* valuesPtr = values)
            {
                CFDictionaryGetKeysAndValues(handle, keysPtr, valuesPtr);
            }

            for (var i = 0; i < count; i++)
            {
                var key = NSString.GetString(keys[i])!;
                if (NSString.TryGetString(values[i]) is { } strVal)
                    dictionary.Add(key, strVal);
                else if (NSDate.TryAsDateTimeOffset(values[i]) is { } dateVal)
                    dictionary.Add(key, dateVal);
                else if (NSNumber.TryAsStringValue(values[i]) is { } numberVal)
                    dictionary.Add(key, numberVal);
                else
                    dictionary.Add(key, GetDescription(values[i]));
            }
        }

        return dictionary;
    }

    private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [DllImport(CoreFoundationLibrary)]
    private static extern long CFDictionaryGetCount(IntPtr dict);
    [DllImport(CoreFoundationLibrary)]
    private static extern unsafe void CFDictionaryGetKeysAndValues(IntPtr dict, IntPtr* keys, IntPtr* values);
}
