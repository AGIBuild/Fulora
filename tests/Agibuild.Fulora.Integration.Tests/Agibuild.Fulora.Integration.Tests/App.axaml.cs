using Agibuild.Fulora.Integration.Tests.ViewModels;
using Agibuild.Fulora.Integration.Tests.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Agibuild.Fulora.Integration.Tests;

public partial class App : Application
{
    // Tab indices (must match MainView.axaml nav order):
    //   0 = Browser (Consumer E2E)
    //   1 = Advanced Features
    //   2 = Platform Smoke (WK or WV2)
    //   3 = Feature E2E

    public override void Initialize()
    {
        AppLog.Initialize();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // In Avalonia 12+, BindingPlugins is internal; duplicate validation is handled automatically.
            var args = desktop.Args ?? Array.Empty<string>();
            var mainVm = new MainViewModel();

            if (args.Contains("--consumer-e2e"))
            {
                mainVm.SelectedTabIndex = 0;
                mainVm.ConsumerE2E.AutoRun = true;
                mainVm.ConsumerE2E.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }
            else if (args.Contains("--advanced-e2e"))
            {
                mainVm.SelectedTabIndex = 1;
                mainVm.AdvancedE2E.AutoRun = true;
                mainVm.AdvancedE2E.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }
            else if (args.Contains("--wk-smoke"))
            {
                mainVm.SelectedTabIndex = 2;
                mainVm.WkWebViewSmoke.AutoRun = true;
                mainVm.WkWebViewSmoke.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }
            else if (args.Contains("--gtk-smoke"))
            {
                mainVm.SelectedTabIndex = 2;
                mainVm.GtkWebViewSmoke.AutoRun = true;
                mainVm.GtkWebViewSmoke.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }
            else if (args.Contains("--wv2-teardown-stress"))
            {
                mainVm.SelectedTabIndex = 2;
                mainVm.WebView2Smoke.AutoRun = true;
                mainVm.WebView2Smoke.AutoRunMode = WebView2AutoRunMode.TeardownStress;
                mainVm.WebView2Smoke.TeardownStressIterations =
                    TryGetIntArg(args, "--wv2-teardown-iterations", defaultValue: 10);
                mainVm.WebView2Smoke.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }
            else if (args.Contains("--wv2-smoke"))
            {
                mainVm.SelectedTabIndex = 2;
                mainVm.WebView2Smoke.AutoRun = true;
                mainVm.WebView2Smoke.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }
            else if (args.Contains("--wv2-feature-seq"))
            {
                mainVm.SelectedTabIndex = 2;
                mainVm.WebView2Smoke.AutoRun = true;
                mainVm.WebView2Smoke.AutoRunCompleted += exitCode =>
                {
                    if (exitCode != 0)
                    {
                        Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
                        return;
                    }
                    Dispatcher.UIThread.Post(() =>
                    {
                        mainVm.FeatureE2E.AutoRun = true;
                        mainVm.SelectedTabIndex = 3;
                    });
                };
                mainVm.FeatureE2E.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }
            else if (args.Contains("--wv2-feature-seq-no-exit"))
            {
                mainVm.SelectedTabIndex = 2;
                mainVm.WebView2Smoke.AutoRun = true;
                mainVm.WebView2Smoke.AutoRunCompleted += exitCode =>
                {
                    if (exitCode != 0)
                    {
                        Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
                        return;
                    }
                    Dispatcher.UIThread.Post(() =>
                    {
                        mainVm.FeatureE2E.AutoRun = true;
                        mainVm.SelectedTabIndex = 3;
                    });
                };
            }
            else if (args.Contains("--feature-e2e"))
            {
                mainVm.SelectedTabIndex = 3;
                mainVm.FeatureE2E.AutoRun = true;
                mainVm.FeatureE2E.AutoRunCompleted += exitCode =>
                    Dispatcher.UIThread.Post(() => desktop.Shutdown(exitCode));
            }

            desktop.MainWindow = new MainWindow { DataContext = mainVm };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static int TryGetIntArg(string[] args, string key, int defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], key, StringComparison.Ordinal) &&
                int.TryParse(args[i + 1], out var value) &&
                value > 0)
            {
                return value;
            }
        }
        return defaultValue;
    }
}
