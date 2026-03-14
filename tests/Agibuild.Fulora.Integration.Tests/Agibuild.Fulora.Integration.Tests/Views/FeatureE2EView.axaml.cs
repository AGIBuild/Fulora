using System;
using System.Globalization;
using Agibuild.Fulora.Integration.Tests.ViewModels;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace Agibuild.Fulora.Integration.Tests.Views;

public partial class FeatureE2EView : UserControl
{
    public FeatureE2EView()
    {
        InitializeComponent();
        Loaded += OnViewLoaded;
    }

    private void OnViewLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= OnViewLoaded;

        if (DataContext is FeatureE2EViewModel vm)
        {
            var webView = this.FindControl<WebView>("WebViewControl");
            if (webView is not null)
            {
                vm.WebViewControl = webView;
                vm.OnWebViewLoaded();
            }
        }
    }
}

/// <summary>
/// Converts a test result status string ("PASS", "FAIL", "SKIP", "...", "—")
/// to a background or foreground brush.
/// <para>ConverterParameter: "bg" for background (default), "fg" for foreground.</para>
/// </summary>
public sealed class TestResultBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush PassBg = new(Color.Parse("#ECFDF5"));
    private static readonly SolidColorBrush FailBg = new(Color.Parse("#FEF2F2"));
    private static readonly SolidColorBrush SkipBg = new(Color.Parse("#FFFBEB"));
    private static readonly SolidColorBrush RunBg = new(Color.Parse("#EFF6FF"));
    private static readonly SolidColorBrush PendingBg = new(Color.Parse("#F1F5F9"));

    private static readonly SolidColorBrush PassFg = new(Color.Parse("#059669"));
    private static readonly SolidColorBrush FailFg = new(Color.Parse("#DC2626"));
    private static readonly SolidColorBrush SkipFg = new(Color.Parse("#D97706"));
    private static readonly SolidColorBrush RunFg = new(Color.Parse("#2563EB"));
    private static readonly SolidColorBrush PendingFg = new(Color.Parse("#94A3B8"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString();
        var mode = parameter?.ToString();

        if (mode == "fg")
        {
            return status switch
            {
                "PASS" => PassFg,
                "FAIL" => FailFg,
                "SKIP" => SkipFg,
                "..." => RunFg,
                _ => PendingFg
            };
        }

        // Default: background
        return status switch
        {
            "PASS" => PassBg,
            "FAIL" => FailBg,
            "SKIP" => SkipBg,
            "..." => RunBg,
            _ => PendingBg
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
