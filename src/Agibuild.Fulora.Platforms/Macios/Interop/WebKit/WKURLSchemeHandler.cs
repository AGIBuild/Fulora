// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

namespace Agibuild.Fulora.Platforms.Macios.Interop.WebKit;

internal sealed class WKURLSchemeHandler
{
    public event EventHandler<WKURLSchemeTaskEventArgs>? StartTask;

    public event EventHandler<WKURLSchemeTaskEventArgs>? StopTask;

    internal void OnStartTask(WKURLSchemeTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        StartTask?.Invoke(this, new WKURLSchemeTaskEventArgs(task));
    }

    internal void OnStopTask(WKURLSchemeTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        StopTask?.Invoke(this, new WKURLSchemeTaskEventArgs(task));
    }
}

internal sealed class WKURLSchemeTaskEventArgs(WKURLSchemeTask task) : EventArgs
{
    public WKURLSchemeTask Task { get; } = task;
}
