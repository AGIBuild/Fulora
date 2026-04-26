// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild
// Newly authored — Fulora-original WKSnapshotConfiguration wrapper for managed screenshot capture.

using Agibuild.Fulora.Platforms.Macios.Interop;

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKSnapshotConfiguration : NSObject
{
    private static readonly IntPtr s_class = WKWebKit.objc_getClass("WKSnapshotConfiguration");

    public WKSnapshotConfiguration() : base(s_class)
    {
        Init();
    }
}
