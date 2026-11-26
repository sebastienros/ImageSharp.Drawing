// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.Fonts;
using SixLabors.Fonts.Rendering;
using SixLabors.ImageSharp.Drawing.Text;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The glyph outlines of one document load: one shared path per font and glyph, which every instance of the glyph places
/// with a transform.
/// </summary>
/// <remarks>
/// <para>
/// A font here is a face at a size with its bold and italic synthesis (see <see cref="SvgFontMatcher.GetFont"/>), so the
/// outline is in user units with its origin at the glyph origin on the alphabetic baseline and the y axis down.
/// Instances of a glyph at the same size and scale also share the flattened geometry that the backend caches per path.
/// </para>
/// <para>
/// Outlines come from <see cref="TextRenderer.RenderTo(IGlyphRenderer, ushort, GlyphOptions)"/> through the glyph builder
/// that <see cref="TextBuilder"/> uses, without hinting. Colour glyphs render as their plain outline: the glyph builders
/// keep the layers of COLR glyphs, but the SVG output draws one paint per glyph, so colour fonts render monochrome, and
/// bitmap glyphs (CBDT, sbix) do not render. The cache is not thread-safe; the resolve stage is single-threaded, and
/// the paths are immutable once built.
/// </para>
/// </remarks>
internal sealed class SvgGlyphOutlines
{
    private readonly Dictionary<(Font Font, ushort Glyph), IPath?> outlines = [];

    /// <summary>
    /// Returns the outline of a glyph.
    /// </summary>
    /// <param name="font">The font.</param>
    /// <param name="glyph">The glyph id.</param>
    /// <returns>The outline, or <see langword="null"/> for a glyph without contours, such as a space.</returns>
    public IPath? Get(Font font, ushort glyph)
    {
        if (this.outlines.TryGetValue((font, glyph), out IPath? outline))
        {
            return outline;
        }

        using (OutlineBuilder builder = new())
        {
            // At 72 dpi a point is a pixel, so the font size in pixels gives the outline in user units.
            GlyphOptions options = new()
            {
                Font = font,
                Dpi = 72F,
                HintingMode = HintingMode.None,
                TextBaseline = TextBaseline.Alphabetic,
            };

            TextRenderer.RenderTo(builder, glyph, options);
            outline = builder.Outline;
        }

        if (outline is not null)
        {
            // Prime the cached closed form, which every fill uses, while the resolve stage is single-threaded.
            _ = outline.AsClosedPath();
        }

        this.outlines.Add((font, glyph), outline);
        return outline;
    }

    /// <summary>
    /// Captures the outline of one glyph.
    /// </summary>
    private sealed class OutlineBuilder : BaseGlyphBuilder
    {
        /// <summary>
        /// Gets the outline of the rendered glyph, or <see langword="null"/> when it has no contour.
        /// </summary>
        public IPath? Outline
        {
            get
            {
                if (this.CurrentPaths.Count == 0)
                {
                    return null;
                }

                IPath path = this.CurrentPaths.Count == 1 ? this.CurrentPaths[0] : new ComplexPolygon([.. this.CurrentPaths]);
                RectangleF bounds = path.Bounds;
                bool hasArea = float.IsFinite(bounds.Width) && float.IsFinite(bounds.Height) && (bounds.Width > 0F || bounds.Height > 0F);
                return hasArea ? path : null;
            }
        }

        /// <inheritdoc/>
        protected override bool CollectsGlyphPaths => false;
    }
}
