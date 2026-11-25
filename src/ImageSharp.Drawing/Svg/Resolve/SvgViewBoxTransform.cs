// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Computes the transform that maps a <c>viewBox</c> into a viewport rectangle, following <c>preserveAspectRatio</c>.
/// </summary>
internal static class SvgViewBoxTransform
{
    /// <summary>
    /// Creates the transform from the coordinates of a view box to the coordinates of the viewport, as the SVG 2
    /// "equivalent transform of an SVG viewport" defines it.
    /// </summary>
    /// <remarks>
    /// The scale is the viewport size over the view box size on each axis. Unless the alignment is <c>none</c>, both axes
    /// take the smaller scale for <c>meet</c> and the larger for <c>slice</c>. The translation moves the view box origin
    /// to the viewport origin, then the min, mid, or max alignment shifts the scaled view box by none, half, or all of the
    /// space left over on each axis. The result only scales and translates.
    /// </remarks>
    /// <param name="viewBox">The view box. Its width and height are greater than zero.</param>
    /// <param name="preserveAspectRatio">The alignment and the meet or slice choice.</param>
    /// <param name="viewport">The viewport rectangle.</param>
    /// <returns>The transform.</returns>
    public static Matrix3x2 Create(RectangleF viewBox, SvgPreserveAspectRatio preserveAspectRatio, RectangleF viewport)
    {
        float scaleX = viewport.Width / viewBox.Width;
        float scaleY = viewport.Height / viewBox.Height;
        SvgAlign align = preserveAspectRatio.Align;

        if (align != SvgAlign.None)
        {
            float scale = preserveAspectRatio.Slice ? MathF.Max(scaleX, scaleY) : MathF.Min(scaleX, scaleY);
            scaleX = scale;
            scaleY = scale;
        }

        float translateX = viewport.X - (viewBox.X * scaleX);
        float translateY = viewport.Y - (viewBox.Y * scaleY);
        float spaceX = viewport.Width - (viewBox.Width * scaleX);
        float spaceY = viewport.Height - (viewBox.Height * scaleY);

        switch (align)
        {
            case SvgAlign.XMidYMin or SvgAlign.XMidYMid or SvgAlign.XMidYMax:
                translateX += spaceX / 2F;
                break;
            case SvgAlign.XMaxYMin or SvgAlign.XMaxYMid or SvgAlign.XMaxYMax:
                translateX += spaceX;
                break;
        }

        switch (align)
        {
            case SvgAlign.XMinYMid or SvgAlign.XMidYMid or SvgAlign.XMaxYMid:
                translateY += spaceY / 2F;
                break;
            case SvgAlign.XMinYMax or SvgAlign.XMidYMax or SvgAlign.XMaxYMax:
                translateY += spaceY;
                break;
        }

        return new Matrix3x2(scaleX, 0F, 0F, scaleY, translateX, translateY);
    }
}
