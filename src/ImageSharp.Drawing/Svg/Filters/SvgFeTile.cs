// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The <c>feTile</c> filter primitive: repeats the subregion of its input over its own subregion.
/// </summary>
/// <remarks>
/// <para>
/// Filter Effects Level 1: the input subregion is one tile, placed at its own position, and repeated in both directions
/// across the subregion of the primitive, whose default is the filter region. A standard input has the filter region as
/// its subregion.
/// </para>
/// <para>
/// Under a scale that puts the edges of the input subregion between pixels, the tile is the input subregion in filter
/// space rounded inward to whole pixels, so the tiles are whole pixels apart: Chromium renders tiles of 29 pixels for an
/// input subregion from 157.5 to 187.5, and of 14 for one from 15.3 to 30.75. The engine rounds the subregion outward
/// when it computes the input, so the inward tile lies inside the input image.
/// </para>
/// <para>
/// The primitive reads its whole input subregion: <see cref="GetInputArea"/> asks for the filter region, which the
/// engine clips to the subregion of the input. Moving pixels does not depend on the color space, so the primitive works
/// in the space of its input.
/// </para>
/// </remarks>
internal sealed class SvgFeTile : SvgFilterPrimitive
{
    /// <summary>
    /// How far an edge of the input subregion in filter space may lie past a whole pixel and still round to it, so that
    /// an edge on a pixel boundary does not lose a pixel to floating point error.
    /// </summary>
    private const float EdgeTolerance = 1E-3F;

    private readonly RectangleF inputSubregion;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFeTile"/> class.
    /// </summary>
    /// <param name="input">The input index.</param>
    /// <param name="inputSubregion">The subregion of the input in user space: the tile.</param>
    private SvgFeTile(int input, RectangleF inputSubregion)
        : base([input])
        => this.inputSubregion = inputSubregion;

    /// <inheritdoc/>
    public override bool PreservesColorSpace => true;

    /// <inheritdoc/>
    public override bool HasFilterRegionAsDefaultSubregion => true;

    /// <summary>
    /// Creates the primitive of an <c>feTile</c> element.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns><see cref="SvgFilterBuildResult.Created"/>.</returns>
    public static SvgFilterBuildResult Create(SvgFilterBuildContext context, out SvgFilterPrimitive? primitive)
    {
        int input = context.GetInput(context.Attributes.In);
        primitive = new SvgFeTile(input, context.GetInputSubregion(input));
        return SvgFilterBuildResult.Created;
    }

    /// <inheritdoc/>
    public override Rectangle GetInputArea(int input, Rectangle area, in SvgFilterRenderContext context) => context.FilterRegion;

    /// <inheritdoc/>
    public override void Render(in SvgFilterRenderContext context, ReadOnlySpan<SvgFilterImage> inputs, SvgFilterImage output)
    {
        // The input subregion rounded inward, inside the pixels that the input holds. The clamp keeps the conversions
        // to int and the width of the rectangle (at most 2^30) defined for a subregion far larger than any image.
        const float limit = 1 << 29;
        SvgFilterImage input = inputs[0];
        RectangleF bounds = context.ToFilter(this.inputSubregion);
        int left = (int)MathF.Ceiling(Math.Clamp(bounds.Left - EdgeTolerance, -limit, limit));
        int top = (int)MathF.Ceiling(Math.Clamp(bounds.Top - EdgeTolerance, -limit, limit));
        int right = (int)MathF.Floor(Math.Clamp(bounds.Right + EdgeTolerance, -limit, limit));
        int bottom = (int)MathF.Floor(Math.Clamp(bounds.Bottom + EdgeTolerance, -limit, limit));
        Rectangle tile = right > left && bottom > top ? Rectangle.Intersect(Rectangle.FromLTRB(left, top, right, bottom), input.Area) : Rectangle.Empty;
        if (tile.Width <= 0 || tile.Height <= 0)
        {
            return;
        }

        Rectangle area = output.Area;
        for (int y = area.Top; y < area.Bottom; y++)
        {
            // The tile row and the tile column of the first output pixel, with a modulo that is never negative.
            ReadOnlySpan<Rgba32> source = input.GetRow(tile.Top + Modulo(y - tile.Top, tile.Height)).Slice(tile.Left - input.Area.Left, tile.Width);
            Span<Rgba32> destination = output.GetRow(y);
            int column = Modulo(area.Left - tile.Left, tile.Width);

            // Copy the tile row in runs: from the current column to the end of the tile, then whole tiles.
            int x = 0;
            while (x < destination.Length)
            {
                int count = Math.Min(tile.Width - column, destination.Length - x);
                source.Slice(column, count).CopyTo(destination[x..]);
                x += count;
                column = 0;
            }
        }
    }

    /// <summary>
    /// Returns the remainder of a division that is never negative.
    /// </summary>
    /// <param name="value">The dividend.</param>
    /// <param name="divisor">The divisor; greater than zero.</param>
    /// <returns>The remainder in the range 0 to <paramref name="divisor"/> - 1.</returns>
    private static int Modulo(int value, int divisor)
    {
        int remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
