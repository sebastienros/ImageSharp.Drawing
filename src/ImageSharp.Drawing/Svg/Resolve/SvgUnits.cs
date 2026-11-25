// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Converts <see cref="SvgLength"/> values to user units.
/// </summary>
/// <remarks>
/// <c>px</c> and unitless numbers are user units. Absolute units convert with the load resolution: an inch is
/// <c>dpi</c> user units, a point 1/72 inch, a pica 1/6 inch. <c>em</c> is the computed font size and <c>ex</c> half of
/// it, because phase 1 has no font metrics. A percentage refers to a size that the caller chooses: the viewport width for
/// x-axis values, the height for y-axis values, and <see cref="Diagonal(SizeF)"/> for other lengths.
/// </remarks>
internal static class SvgUnits
{
    /// <summary>
    /// Converts a length to user units.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="percentBase">The size that 100% refers to.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <param name="defaultValue">The value of an unspecified length.</param>
    /// <returns>The length in user units.</returns>
    public static float ToUser(SvgLength value, float percentBase, float fontSize, float dpi, float defaultValue)
    {
        if (!value.IsSpecified)
        {
            return defaultValue;
        }

        return value.Unit == SvgLengthUnit.Percent ? value.Value * percentBase / 100F : ToPixels(value, fontSize, dpi);
    }

    /// <summary>
    /// Converts a length that is not a percentage to user units. An unspecified length is zero.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="fontSize">The font size that <c>em</c> and <c>ex</c> refer to.</param>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <returns>The length in user units.</returns>
    public static float ToPixels(SvgLength value, float fontSize, float dpi)
        => value.Unit switch
        {
            SvgLengthUnit.Number or SvgLengthUnit.Px => value.Value,
            SvgLengthUnit.Pt => value.Value * dpi / 72F,
            SvgLengthUnit.Pc => value.Value * dpi / 6F,
            SvgLengthUnit.Mm => value.Value * dpi / 25.4F,
            SvgLengthUnit.Cm => value.Value * dpi / 2.54F,
            SvgLengthUnit.In => value.Value * dpi,
            SvgLengthUnit.Em => value.Value * fontSize,
            SvgLengthUnit.Ex => value.Value * fontSize / 2F,
            _ => 0F
        };

    /// <summary>
    /// Returns the size that a percentage of a length that is neither horizontal nor vertical refers to, as SVG defines:
    /// <c>sqrt((w² + h²) / 2)</c>.
    /// </summary>
    /// <param name="viewport">The viewport size.</param>
    /// <returns>The reference size.</returns>
    public static float Diagonal(SizeF viewport)
        => MathF.Sqrt(((viewport.Width * viewport.Width) + (viewport.Height * viewport.Height)) / 2F);
}
