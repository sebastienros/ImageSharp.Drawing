// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The substitute of a disabled primitive whose result is transparent black, as Filter Effects defines for a primitive
/// subregion with a zero or negative width or height.
/// </summary>
internal sealed class SvgFeTransparent : SvgFilterPrimitive
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeTransparent"/> class.
    /// </summary>
    public SvgFeTransparent()
        : base([])
    {
    }

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        // The engine allocates the output transparent.
    }
}
