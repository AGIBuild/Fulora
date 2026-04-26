using Agibuild.Fulora;
using Agibuild.Fulora.Platforms.Macios;
using Xunit;

namespace Agibuild.Fulora.Platforms.UnitTests.Macios;

public sealed class ApplePdfPrintOptionsTests
{
    [Fact]
    public void ThrowIfUnsupported_allows_null_and_default_options()
    {
        ApplePdfPrintOptions.ThrowIfUnsupported(null);
        ApplePdfPrintOptions.ThrowIfUnsupported(new PdfPrintOptions());
    }

    [Theory]
    [InlineData(nameof(PdfPrintOptions.Landscape))]
    [InlineData(nameof(PdfPrintOptions.PageWidth))]
    [InlineData(nameof(PdfPrintOptions.PageHeight))]
    [InlineData(nameof(PdfPrintOptions.MarginTop))]
    [InlineData(nameof(PdfPrintOptions.MarginBottom))]
    [InlineData(nameof(PdfPrintOptions.MarginLeft))]
    [InlineData(nameof(PdfPrintOptions.MarginRight))]
    [InlineData(nameof(PdfPrintOptions.Scale))]
    [InlineData(nameof(PdfPrintOptions.PrintBackground))]
    public void ThrowIfUnsupported_rejects_non_default_options(string property)
    {
        var options = new PdfPrintOptions();
        switch (property)
        {
            case nameof(PdfPrintOptions.Landscape):
                options.Landscape = true;
                break;
            case nameof(PdfPrintOptions.PageWidth):
                options.PageWidth = 4;
                break;
            case nameof(PdfPrintOptions.PageHeight):
                options.PageHeight = 4;
                break;
            case nameof(PdfPrintOptions.MarginTop):
                options.MarginTop = 0;
                break;
            case nameof(PdfPrintOptions.MarginBottom):
                options.MarginBottom = 0;
                break;
            case nameof(PdfPrintOptions.MarginLeft):
                options.MarginLeft = 0;
                break;
            case nameof(PdfPrintOptions.MarginRight):
                options.MarginRight = 0;
                break;
            case nameof(PdfPrintOptions.Scale):
                options.Scale = 0.5;
                break;
            case nameof(PdfPrintOptions.PrintBackground):
                options.PrintBackground = false;
                break;
        }

        Assert.Throws<NotSupportedException>(() => ApplePdfPrintOptions.ThrowIfUnsupported(options));
    }
}
