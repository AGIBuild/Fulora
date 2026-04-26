// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.

using System;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal class NSArray(IntPtr handle, bool owns) : NSObject(handle, owns)
{
    private static readonly IntPtr s_class = Foundation.objc_getClass("NSArray");
    private static readonly IntPtr s_arrayWithObjectsCount = Libobjc.sel_getUid("arrayWithObjects:count:");
    private static readonly IntPtr s_count = Libobjc.sel_getUid("count");
    private static readonly IntPtr s_getObjects = Libobjc.sel_getUid("getObjects:range:");
    private static readonly IntPtr s_objectAtIndex = Libobjc.sel_getUid("objectAtIndex:");

    public nint Count => Libobjc.intptr_objc_msgSend(Handle, s_count);

    public void GetObjects(IntPtr objects, nint from, nint length)
    {
        Libobjc.void_objc_msgSend(Handle, s_getObjects, objects, from, length);
    }

    public IntPtr ObjectAtIndex(nuint index) => Libobjc.intptr_objc_msgSend(Handle, s_objectAtIndex, index);

    public static unsafe NSArray FromHandles(params IntPtr[] handles)
    {
        fixed (IntPtr* objects = handles)
        {
            var array = Libobjc.intptr_objc_msgSend_intptr_nuint(
                s_class,
                s_arrayWithObjectsCount,
                new IntPtr(objects),
                (nuint)handles.Length);
            return new NSArray(array, owns: false);
        }
    }
}
