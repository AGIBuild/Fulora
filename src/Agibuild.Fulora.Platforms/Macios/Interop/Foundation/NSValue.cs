// SPDX-License-Identifier: MIT
// Copyright (c) 2026 AvaloniaUI OÜ
// Copyright (c) 2026 Agibuild
// Vendored from Avalonia.Controls.WebView; see Macios/ATTRIBUTION.md.

using System;

namespace Agibuild.Fulora.Platforms.Macios.Interop.Foundation;

internal abstract class NSValue(IntPtr handle, bool owns) : NSObject(handle, owns);
