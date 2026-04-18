namespace Agibuild.Fulora;

/// <summary>
/// Capability: render the current page to a PDF byte stream.
/// </summary>
public interface IWebViewPrinting
{
    /// <summary>
    /// Prints the current page to PDF. When <paramref name="options"/> is
    /// <see langword="null"/> the adapter's platform defaults apply.
    /// </summary>
    Task<byte[]> PrintToPdfAsync(PdfPrintOptions? options = null);
}
