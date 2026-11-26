// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A specified <c>filter</c> value other than <c>none</c>: one or more <c>url()</c> references and filter functions, in
/// the order in which they apply. The instance is immutable and shared by every style that refers to it.
/// </summary>
internal sealed class SvgFilterList
{
    private readonly SvgFilterFunction[] functions;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterList"/> class. The list takes ownership of the array.
    /// </summary>
    /// <param name="functions">The entries; at least one.</param>
    public SvgFilterList(SvgFilterFunction[] functions) => this.functions = functions;

    /// <summary>
    /// Gets the entries in the order in which they apply.
    /// </summary>
    public ReadOnlySpan<SvgFilterFunction> Functions => this.functions;
}
