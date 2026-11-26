// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The computed CSS <c>transform</c>, and the computed form of math functions in text lengths.
/// </content>
internal partial struct SvgComputedStyle
{
    /// <summary>
    /// Converts a specified length of a text property (<c>letter-spacing</c>, <c>word-spacing</c>, <c>kerning</c>,
    /// <c>baseline-shift</c>) to its computed form. A plain percentage stays a percentage of the font size of the element
    /// that uses it; a math function that mixes a percentage with lengths resolves here, against the font size of this
    /// element, because the text layout reads only pixels and percentages.
    /// </summary>
    /// <param name="value">The specified length; unspecified stays unspecified.</param>
    /// <param name="fontSize">The computed font size of the element.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <returns>The computed length.</returns>
    private static SvgLength ToComputedTextLength(SvgLength value, float fontSize, SvgUnitBasis units)
        => value.Unit == SvgLengthUnit.Calc
            ? new SvgLength(SvgUnits.ToUser(value, fontSize, fontSize, units, 0F), SvgLengthUnit.Px)
            : ToComputedOptionalLength(value, fontSize, units);

    /// <summary>
    /// Computes the CSS <c>transform</c> of an element that specifies the property.
    /// </summary>
    /// <remarks>
    /// <c>inherit</c> takes the computed value of the parent, which is <see langword="null"/> when the parent has only the
    /// attribute: the reader copied the transform table entry of the parent then. <c>initial</c> and <c>unset</c> (the
    /// property does not inherit) give <see langword="null"/>, for which the reader removed the attribute transform.
    /// </remarks>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="values">The specified geometry values, or <see langword="null"/>.</param>
    /// <param name="parent">The computed geometry values of the parent.</param>
    /// <returns>The computed value, or <see langword="null"/> when the transform table of the element holds it.</returns>
    private static SvgCssTransform? ComputeCssTransform(in SvgSpecifiedStyle specified, SvgGeometryStyle? values, SvgGeometryStyle parent)
    {
        if (specified.IsInherit(SvgPropertyId.Transform))
        {
            return parent.CssTransform;
        }

        return specified.HasValue(SvgPropertyId.Transform) ? values?.CssTransform : null;
    }
}
