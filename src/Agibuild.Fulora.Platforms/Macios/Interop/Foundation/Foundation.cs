// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.

using System;
using System.Runtime.InteropServices;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal static partial class Foundation
{
    private const string Framework = "/System/Library/Frameworks/Foundation.framework/Foundation";

    [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getClass(string className);
    [LibraryImport(Framework, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getProtocol(string name);
}
