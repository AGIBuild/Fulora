// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Agibuild

using Agibuild.Fulora;

namespace Agibuild.Fulora.Platforms.Macios;

internal static class ApplePdfPrintOptions
{
    public static void ThrowIfUnsupported(PdfPrintOptions? options)
    {
        if (options is null || IsDefault(options))
        {
            return;
        }

        throw new NotSupportedException(
            "Apple WKWebView PDF export currently supports only default PdfPrintOptions.");
    }

    private static bool IsDefault(PdfPrintOptions options)
    {
        var defaults = new PdfPrintOptions();
        return options.Landscape == defaults.Landscape &&
               options.PageWidth.Equals(defaults.PageWidth) &&
               options.PageHeight.Equals(defaults.PageHeight) &&
               options.MarginTop.Equals(defaults.MarginTop) &&
               options.MarginBottom.Equals(defaults.MarginBottom) &&
               options.MarginLeft.Equals(defaults.MarginLeft) &&
               options.MarginRight.Equals(defaults.MarginRight) &&
               options.Scale.Equals(defaults.Scale) &&
               options.PrintBackground == defaults.PrintBackground;
    }
}
