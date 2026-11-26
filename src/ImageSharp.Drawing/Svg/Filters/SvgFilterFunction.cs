// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// One entry of a specified <c>filter</c> value: a <c>url()</c> reference or a filter function with its arguments, as
/// parsed. Defaults for missing arguments are already applied; clamping that Filter Effects defines at computed value
/// time (such as <c>grayscale(2)</c> acting as <c>grayscale(1)</c>) is left to the consumer.
/// </summary>
internal readonly struct SvgFilterFunction
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgFilterFunction"/> struct.
    /// </summary>
    /// <param name="kind">The kind.</param>
    /// <param name="reference">For <see cref="SvgFilterFunctionKind.Url"/>, the reference.</param>
    /// <param name="amount">The number argument; see <see cref="Amount"/>.</param>
    /// <param name="offsetX">For <see cref="SvgFilterFunctionKind.DropShadow"/>, the horizontal offset.</param>
    /// <param name="offsetY">For <see cref="SvgFilterFunctionKind.DropShadow"/>, the vertical offset.</param>
    /// <param name="radius">For <see cref="SvgFilterFunctionKind.Blur"/> and <see cref="SvgFilterFunctionKind.DropShadow"/>, the standard deviation.</param>
    /// <param name="color">For <see cref="SvgFilterFunctionKind.DropShadow"/>, the color.</param>
    public SvgFilterFunction(
        SvgFilterFunctionKind kind,
        SvgReference reference,
        float amount,
        SvgLength offsetX,
        SvgLength offsetY,
        SvgLength radius,
        SvgPaint color)
    {
        this.Kind = kind;
        this.Reference = reference;
        this.Amount = amount;
        this.OffsetX = offsetX;
        this.OffsetY = offsetY;
        this.Radius = radius;
        this.Color = color;
    }

    /// <summary>
    /// Gets the kind.
    /// </summary>
    public SvgFilterFunctionKind Kind { get; }

    /// <summary>
    /// Gets the reference of a <see cref="SvgFilterFunctionKind.Url"/> entry. Its <see cref="SvgReference.Id"/> is
    /// <see langword="null"/> for a URL that is not a same-document fragment.
    /// </summary>
    public SvgReference Reference { get; }

    /// <summary>
    /// Gets the number argument: a fraction for the functions that take a number or a percentage (<c>50%</c> is 0.5; the
    /// default is 1), and the angle in degrees for <c>hue-rotate()</c> (the default is 0). Never negative for the
    /// former.
    /// </summary>
    public float Amount { get; }

    /// <summary>
    /// Gets the horizontal offset of <c>drop-shadow()</c>.
    /// </summary>
    public SvgLength OffsetX { get; }

    /// <summary>
    /// Gets the vertical offset of <c>drop-shadow()</c>.
    /// </summary>
    public SvgLength OffsetY { get; }

    /// <summary>
    /// Gets the standard deviation of <c>blur()</c> or the blur radius of <c>drop-shadow()</c>; zero when it is missing.
    /// Never negative.
    /// </summary>
    public SvgLength Radius { get; }

    /// <summary>
    /// Gets the color of <c>drop-shadow()</c>: a paint of kind <see cref="SvgPaintKind.Color"/> or
    /// <see cref="SvgPaintKind.CurrentColor"/>, which is the default.
    /// </summary>
    public SvgPaint Color { get; }
}
