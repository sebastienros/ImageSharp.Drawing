// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The state of the resolve stage for text: the font matcher, the glyph outlines, the cached layouts, and the character
/// count. The resolver creates it on the first text element, so documents without text pay one null field for it.
/// </summary>
internal sealed class SvgTextResolveState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextResolveState"/> class.
    /// </summary>
    /// <param name="options">The load options, which name the fonts.</param>
    public SvgTextResolveState(SvgLoadOptions options) => this.FontMatcher = new SvgFontMatcher(options);

    /// <summary>
    /// Gets the font matcher of the load.
    /// </summary>
    public SvgFontMatcher FontMatcher { get; }

    /// <summary>
    /// Gets the glyph outlines of the load.
    /// </summary>
    public SvgGlyphOutlines Outlines { get; } = new();

    /// <summary>
    /// Gets the cached layout of each <c>text</c> element, with the computed style and the viewport it was laid out for.
    /// </summary>
    public Dictionary<int, (SvgComputedStyle Style, SizeF Viewport, SvgTextLayout Layout)> Layouts { get; } = [];

    /// <summary>
    /// Gets the clip geometry of each layout that a <c>clipPath</c> uses.
    /// </summary>
    public Dictionary<SvgTextLayout, IPath?> ClipPaths { get; } = [];

    /// <summary>
    /// Gets or sets the number of characters laid out so far, counted per instance.
    /// </summary>
    public long Characters { get; set; }

    /// <summary>
    /// Gets or sets the layout of the <c>text</c> element whose nodes resolve, so that the bounding box of a
    /// <c>tspan</c> comes from it; <see langword="null"/> outside text.
    /// </summary>
    public SvgTextLayout? ActiveLayout { get; set; }

    /// <summary>
    /// Gets the measured paths of <c>textPath</c>, by the element that holds the geometry (a <c>path</c>, or a
    /// <c>textPath</c> with a <c>path</c> attribute), the flattening tolerance, and the viewport. Many <c>textPath</c>
    /// elements that reference one path measure it once.
    /// </summary>
    public Dictionary<(int Element, float Tolerance, SizeF Viewport), SvgTextPathMeasure?> PathMeasures { get; } = [];
}
