// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A CSS <c>&lt;position&gt;</c>, such as the center of <c>circle()</c>: an offset along each axis, measured from the
/// left or right edge and from the top or bottom edge of the reference box.
/// </summary>
/// <remarks>
/// Keywords are stored as percentages from the start edge: <c>left</c> and <c>top</c> 0%, <c>center</c> 50%,
/// <c>right</c> and <c>bottom</c> 100%. Only the four-value form, such as <c>right 10px bottom 20%</c>, measures from
/// the end edge, which <see cref="XFromEnd"/> and <see cref="YFromEnd"/> say.
/// </remarks>
internal readonly struct SvgShapePosition
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgShapePosition"/> struct.
    /// </summary>
    /// <param name="x">The horizontal offset.</param>
    /// <param name="xFromEnd">Whether <paramref name="x"/> is measured from the right edge.</param>
    /// <param name="y">The vertical offset.</param>
    /// <param name="yFromEnd">Whether <paramref name="y"/> is measured from the bottom edge.</param>
    public SvgShapePosition(SvgLength x, bool xFromEnd, SvgLength y, bool yFromEnd)
    {
        this.X = x;
        this.XFromEnd = xFromEnd;
        this.Y = y;
        this.YFromEnd = yFromEnd;
    }

    /// <summary>
    /// Gets the center of the reference box, the default position.
    /// </summary>
    public static SvgShapePosition Center { get; } =
        new(new SvgLength(50F, SvgLengthUnit.Percent), false, new SvgLength(50F, SvgLengthUnit.Percent), false);

    /// <summary>
    /// Gets the horizontal offset: a length, or a percentage of the width of the reference box.
    /// </summary>
    public SvgLength X { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="X"/> is measured from the right edge.
    /// </summary>
    public bool XFromEnd { get; }

    /// <summary>
    /// Gets the vertical offset: a length, or a percentage of the height of the reference box.
    /// </summary>
    public SvgLength Y { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Y"/> is measured from the bottom edge.
    /// </summary>
    public bool YFromEnd { get; }
}
