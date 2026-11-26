// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// Custom properties and declarations with <c>var()</c>.
/// </content>
internal sealed partial class SvgPropertyParser
{
    /// <summary>
    /// Gets the text properties that the <c>font-variant</c> declaration sets besides <see cref="SvgPropertyId.FontVariant"/>,
    /// as the CSS shorthand of these longhands.
    /// </summary>
    private static ReadOnlySpan<SvgPropertyId> FontVariantLonghands =>
    [
        SvgPropertyId.FontVariantLigatures, SvgPropertyId.FontVariantCaps, SvgPropertyId.FontVariantNumeric,
        SvgPropertyId.FontVariantEastAsian, SvgPropertyId.FontVariantPosition
    ];

    /// <summary>
    /// Returns whether a CSS declaration name is a custom property name: it starts with two hyphens.
    /// </summary>
    /// <param name="name">The declaration name, with its escapes decoded.</param>
    /// <returns><see langword="true"/> for a custom property name; otherwise, <see langword="false"/>.</returns>
    public static bool IsCustomPropertyName(ReadOnlySpan<char> name) => name.Length > 2 && name[0] == '-' && name[1] == '-';

    /// <summary>
    /// Stores a custom property declaration (<c>--name: value</c>) in a style. Every value is valid: CSS keeps the tokens
    /// of a custom property until a <c>var()</c> reference uses them. The CSS-wide keywords alone are keywords.
    /// </summary>
    /// <param name="name">The name, with its leading <c>--</c>. Custom property names are case-sensitive.</param>
    /// <param name="value">The value text without <c>!important</c>; it may be empty.</param>
    /// <param name="style">The style that receives the declaration.</param>
    public static void SetCustomProperty(ReadOnlySpan<char> name, ReadOnlySpan<char> value, ref SvgSpecifiedStyle style)
    {
        value = SvgNumberParser.Trim(value);
        SvgCustomPropertyValue custom = value switch
        {
            _ when value.Equals("initial", StringComparison.OrdinalIgnoreCase) => new SvgCustomPropertyValue(SvgCustomPropertyKind.Initial, string.Empty),
            _ when value.Equals("inherit", StringComparison.OrdinalIgnoreCase) || value.Equals("unset", StringComparison.OrdinalIgnoreCase)
                => new SvgCustomPropertyValue(SvgCustomPropertyKind.Inherit, string.Empty),
            _ => new SvgCustomPropertyValue(SvgCustomPropertyKind.Text, value.ToString())
        };

        style.SetCustomProperty(name.ToString(), custom);
    }

    /// <summary>
    /// Returns whether a value contains a <c>var()</c> function, whose name CSS matches ignoring ASCII case. A name such
    /// as <c>myvar(</c> is another function.
    /// </summary>
    /// <param name="source">The value.</param>
    /// <returns><see langword="true"/> if the value has a <c>var(</c> function token; otherwise, <see langword="false"/>.</returns>
    internal static bool ContainsVar(ReadOnlySpan<char> source)
    {
        // Most values have no parenthesis, so the vectorized search for one ends the test early.
        int offset = 0;
        while (true)
        {
            int open = source[offset..].IndexOf('(');
            if (open < 0)
            {
                return false;
            }

            open += offset;
            if (open >= 3 && source.Slice(open - 3, 3).Equals("var", StringComparison.OrdinalIgnoreCase))
            {
                char before = open > 3 ? source[open - 4] : ' ';
                if (!(char.IsAsciiLetterOrDigit(before) || before is '-' or '_' || before > 0x7F))
                {
                    return true;
                }
            }

            offset = open + 1;
        }
    }

    /// <summary>
    /// Records a declaration of a property with <c>var()</c> as pending, with the longhands that the declaration sets
    /// along with it.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="source">The trimmed value.</param>
    /// <param name="style">The style that receives the declaration.</param>
    private static void MarkPending(SvgPropertyId property, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        string text = source.ToString();
        style.MarkPending(property, text, null);
        switch (property)
        {
            case SvgPropertyId.FontVariant:
                foreach (SvgPropertyId longhand in FontVariantLonghands)
                {
                    style.MarkPending(longhand, text, SvgShorthand.FontVariant);
                }

                break;
            case SvgPropertyId.WhiteSpace:
                style.MarkPending(SvgPropertyId.WhiteSpaceCollapse, text, SvgShorthand.WhiteSpace);
                break;
        }
    }

    /// <summary>
    /// Records a declaration of <c>marker</c>, <c>font</c>, or <c>text-decoration</c> with <c>var()</c> as pending for
    /// every longhand of the shorthand.
    /// </summary>
    /// <param name="shorthand">The shorthand.</param>
    /// <param name="source">The trimmed value.</param>
    /// <param name="style">The style that receives the declaration.</param>
    private static void MarkPendingShorthand(SvgShorthand shorthand, ReadOnlySpan<char> source, ref SvgSpecifiedStyle style)
    {
        string text = source.ToString();
        ReadOnlySpan<SvgPropertyId> longhands = shorthand switch
        {
            SvgShorthand.Marker => MarkerShorthandLonghands,
            SvgShorthand.Font => FontShorthandLonghands,
            _ => TextDecorationShorthandLonghands
        };

        foreach (SvgPropertyId longhand in longhands)
        {
            style.MarkPending(longhand, text, shorthand);
        }
    }
}
