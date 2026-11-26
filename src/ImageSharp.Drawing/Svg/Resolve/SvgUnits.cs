// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Converts <see cref="SvgLength"/> values to user units.
/// </summary>
/// <remarks>
/// <para>
/// <c>px</c> and unitless numbers are user units. Absolute units convert with the load resolution: an inch is
/// <c>dpi</c> user units, a point 1/72 inch, a pica 1/6 inch, a quarter-millimeter 1/101.6 inch. <c>em</c> is the
/// computed font size. <c>ch</c> is the advance of "0" in the first available font of
/// <see cref="SvgUnitBasis.Font"/>, as CSS Values 4 defines it, and half the font size when the basis names no font or
/// the font has no "0" (the fallback of CSS Values 4). <c>ex</c> is half the font size: the x-height of the fonts would
/// change the result of conformance tests whose reference images use that fallback (see <c>SVG_DESIGN.md</c>).
/// <c>rem</c> is the computed font size of the root element. <c>vw</c>, <c>vh</c>, <c>vmin</c>, and <c>vmax</c> refer to
/// the intrinsic size of the document, which plays the part of the initial containing block of a standalone document.
/// A math function that mixes units (<see cref="SvgLengthUnit.Calc"/>) runs its expression with the same references.
/// </para>
/// <para>
/// A percentage refers to a size that the caller chooses: the viewport width for x-axis values, the height for y-axis
/// values, and <see cref="Diagonal(SizeF)"/> for other lengths.
/// </para>
/// </remarks>
internal static class SvgUnits
{
    /// <summary>
    /// Converts a length to user units.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="percentBase">The size that 100% refers to.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="units">The document values that units refer to.</param>
    /// <param name="defaultValue">The value of an unspecified length.</param>
    /// <returns>The length in user units.</returns>
    public static float ToUser(SvgLength value, float percentBase, float fontSize, SvgUnitBasis units, float defaultValue)
    {
        if (!value.IsSpecified)
        {
            return defaultValue;
        }

        return value.Unit switch
        {
            SvgLengthUnit.Percent => value.Value * percentBase / 100F,
            SvgLengthUnit.Calc => EvaluateCalc(value, percentBase, fontSize, units),
            _ => ToPixels(value, fontSize, units)
        };
    }

    /// <summary>
    /// Returns whether a length depends on the size that percentages refer to: a percentage, or a math function with a
    /// percentage in it.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="units">The document values, whose context holds the math functions.</param>
    /// <returns><see langword="true"/> if the length needs a percentage base; otherwise, <see langword="false"/>.</returns>
    public static bool HasPercentage(SvgLength value, SvgUnitBasis units)
        => value.Unit == SvgLengthUnit.Percent
        || (value.Unit == SvgLengthUnit.Calc && units.Context?.GetCalc(value)?.HasPercentage != false);

    /// <summary>
    /// Runs the expression of a math function that mixes units.
    /// </summary>
    /// <param name="value">A length of unit <see cref="SvgLengthUnit.Calc"/>.</param>
    /// <param name="percentBase">The size that 100% refers to.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="units">The document values that units refer to.</param>
    /// <returns>The length in user units; zero when the expression is not known.</returns>
    private static float EvaluateCalc(SvgLength value, float percentBase, float fontSize, SvgUnitBasis units)
        => units.Context?.GetCalc(value)?.Evaluate(percentBase, fontSize, units) ?? 0F;

    /// <summary>
    /// Converts a length that is not a percentage to user units. An unspecified length is zero.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="fontSize">The font size that <c>em</c>, <c>ex</c>, and <c>ch</c> refer to.</param>
    /// <param name="units">The document values that units refer to.</param>
    /// <returns>The length in user units.</returns>
    public static float ToPixels(SvgLength value, float fontSize, SvgUnitBasis units)
        => value.Unit switch
        {
            SvgLengthUnit.Number or SvgLengthUnit.Px => value.Value,
            SvgLengthUnit.Pt => value.Value * units.Dpi / 72F,
            SvgLengthUnit.Pc => value.Value * units.Dpi / 6F,
            SvgLengthUnit.Mm => value.Value * units.Dpi / 25.4F,
            SvgLengthUnit.Cm => value.Value * units.Dpi / 2.54F,
            SvgLengthUnit.In => value.Value * units.Dpi,
            SvgLengthUnit.Q => value.Value * units.Dpi / 101.6F,
            SvgLengthUnit.Em => value.Value * fontSize,
            SvgLengthUnit.Ex => value.Value * fontSize / 2F,
            SvgLengthUnit.Ch => value.Value * fontSize * (units.Font is SvgTextStyle font ? units.Context!.GetZeroAdvance(font) : 0.5F),
            SvgLengthUnit.Rem => value.Value * units.RootFontSize,
            SvgLengthUnit.Vw => value.Value * units.ViewportSize.Width / 100F,
            SvgLengthUnit.Vh => value.Value * units.ViewportSize.Height / 100F,
            SvgLengthUnit.Vmin => value.Value * MathF.Min(units.ViewportSize.Width, units.ViewportSize.Height) / 100F,
            SvgLengthUnit.Vmax => value.Value * MathF.Max(units.ViewportSize.Width, units.ViewportSize.Height) / 100F,

            // A percentage has no base here; the callers of this method do not take percentages.
            SvgLengthUnit.Calc => EvaluateCalc(value, 0F, fontSize, units),
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
