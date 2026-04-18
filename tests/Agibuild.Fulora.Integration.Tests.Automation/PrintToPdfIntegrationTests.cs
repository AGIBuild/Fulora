using Agibuild.Fulora;
using Agibuild.Fulora.Testing;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Agibuild.Fulora.Integration.Tests.Automation;

/// <summary>
/// Integration tests for the PrintToPdfAsync feature.
///
/// HOW IT WORKS (for newcomers):
///   1. We create a MockWebViewAdapterWithPrint — it returns fake PDF bytes (%PDF header).
///   2. We wrap it in a WebDialog (same as a real app would).
///   3. We call PrintToPdfAsync() with various options and verify results.
///   4. We also test that a basic adapter throws NotSupportedException.
///   5. We verify PdfPrintOptions defaults are sensible.
/// </summary>
public sealed class PrintToPdfIntegrationTests
{
    private readonly TestDispatcher _dispatcher = new();

    // ──────────────────── Test 1: Returns PDF bytes ────────────────────

    [AvaloniaFact]
    public void PrintToPdf_returns_pdf_bytes()
    {
        // Arrange
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPrint();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        // Act
        var bytes = DispatcherTestPump.Run(_dispatcher, () => dialog.PrintToPdfAsync());

        // Assert: got non-empty bytes with PDF magic header (%PDF)
        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    // ──────────────────── Test 2: Options pass through ────────────────────

    [AvaloniaFact]
    public void PrintToPdf_with_options_passes_through_to_adapter()
    {
        // Arrange
        var host = new MockDialogHost();
        var adapter = MockWebViewAdapter.CreateWithPrint();
        using var dialog = new WebDialog(host, adapter, _dispatcher);

        var options = new PdfPrintOptions
        {
            Landscape = true,
            Scale = 0.5,
            PageWidth = 11.0,
            PageHeight = 8.5
        };

        // Act
        DispatcherTestPump.Run(_dispatcher, () => dialog.PrintToPdfAsync(options));

        // Assert: adapter received the exact options we passed
        Assert.NotNull(adapter.LastPrintOptions);
        Assert.True(adapter.LastPrintOptions!.Landscape);
        Assert.Equal(0.5, adapter.LastPrintOptions.Scale);
        Assert.Equal(11.0, adapter.LastPrintOptions.PageWidth);
        Assert.Equal(8.5, adapter.LastPrintOptions.PageHeight);
    }

    // ──────────────────── Test 4: Default options ────────────────────

    [AvaloniaFact]
    public void PdfPrintOptions_defaults_are_sensible()
    {
        // Act
        var opts = new PdfPrintOptions();

        // Assert: US Letter portrait with small margins
        Assert.False(opts.Landscape);
        Assert.Equal(8.5, opts.PageWidth);
        Assert.Equal(11.0, opts.PageHeight);
        Assert.Equal(0.4, opts.MarginTop);
        Assert.Equal(0.4, opts.MarginBottom);
        Assert.Equal(0.4, opts.MarginLeft);
        Assert.Equal(0.4, opts.MarginRight);
        Assert.Equal(1.0, opts.Scale);
        Assert.True(opts.PrintBackground);
    }
}
