// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The conditional processing attributes of one element: <c>systemLanguage</c>, <c>requiredExtensions</c>, and
/// <c>requiredFeatures</c>. Each holds the attribute value as written, or <see langword="null"/> when the attribute is
/// absent. An empty string means the attribute is present with an empty value, which differs from absent: SVG makes an
/// empty <c>systemLanguage</c> fail. The reader does not evaluate the conditions.
/// </summary>
internal struct SvgConditionalAttributes
{
    /// <summary>
    /// Gets or sets the raw <c>systemLanguage</c> value, a comma-separated list of language tags, or
    /// <see langword="null"/> when the attribute is absent.
    /// </summary>
    public string? SystemLanguage { readonly get; set; }

    /// <summary>
    /// Gets or sets the raw <c>requiredExtensions</c> value, a whitespace-separated list of URLs, or
    /// <see langword="null"/> when the attribute is absent.
    /// </summary>
    public string? RequiredExtensions { readonly get; set; }

    /// <summary>
    /// Gets or sets the raw <c>requiredFeatures</c> value, or <see langword="null"/> when the attribute is absent. SVG 2
    /// removed the attribute.
    /// </summary>
    public string? RequiredFeatures { readonly get; set; }
}
