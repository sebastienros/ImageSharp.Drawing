// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The substitute of a disabled primitive whose result is its input, as Filter Effects defines for example for a zero
/// <c>stdDeviation</c> or a <c>values</c> list of the wrong length on <c>feColorMatrix</c>. The result is clipped to the
/// subregion of the primitive, as any result.
/// </summary>
internal sealed class SvgFePassThrough : SvgFilterPrimitive
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFePassThrough"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    public SvgFePassThrough(int input)
        : base([input])
    {
    }

    /// <inheritdoc/>
    public override bool PreservesColorSpace => true;

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        Rectangle area = output.Area;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            inputs[0].CopyRow(y, area.Left, output.GetRow(y));
        }
    }
}
