// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses CSS color values: hexadecimal notation, the CSS named colors, <c>transparent</c>, and the
/// <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>, and <c>hsla()</c> functions in both the comma and the space syntax of
/// CSS Color Level 4. Names and function names are ASCII case-insensitive. The parser never throws.
/// </summary>
/// <remarks>
/// Hexadecimal and named colors go through <see cref="Color.TryParseHex(string, out Color, ColorHexFormat)"/> and
/// <see cref="Color.TryParse(string, out Color, ColorHexFormat)"/>. Those APIs take a <see cref="string"/>, so a
/// <see cref="SvgColorCache"/> keeps the results by source text and the common case of a repeated color does not allocate.
/// The functional forms are parsed here, because ImageSharp does not parse them.
/// </remarks>
internal static class SvgColorParser
{
    /// <summary>
    /// The ASCII hexadecimal digits.
    /// </summary>
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>
    /// The ASCII letters.
    /// </summary>
    private static readonly SearchValues<char> Letters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");

    /// <summary>
    /// The unit of a color function component.
    /// </summary>
    private enum ComponentUnit : byte
    {
        /// <summary>
        /// A plain number.
        /// </summary>
        Number,

        /// <summary>
        /// A percentage.
        /// </summary>
        Percent,

        /// <summary>
        /// An angle, converted to degrees.
        /// </summary>
        Angle
    }

    /// <summary>
    /// Parses a color.
    /// </summary>
    /// <param name="source">The characters to parse.</param>
    /// <param name="cache">The cache for hexadecimal and named colors, or <see langword="null"/> to parse without one.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the color.</param>
    /// <returns><see langword="true"/> if the value is a color; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, SvgColorCache? cache, out Color value)
    {
        source = SvgNumberParser.Trim(source);
        if (source.IsEmpty)
        {
            value = default;
            return false;
        }

        if (source[0] == '#')
        {
            if (!IsHexColor(source[1..]))
            {
                value = default;
                return false;
            }

            return cache?.TryGet(source, out value) == true || TryParseHexOrName(source, cache, out value);
        }

        if (source[^1] == ')')
        {
            return TryParseFunction(source, out value);
        }

        // ImageSharp falls back to hexadecimal when a name is unknown, so "bad" would parse as #bbaadd. No CSS color name
        // consists of hexadecimal digits only, so such input is rejected here before it reaches ImageSharp.
        if (!IsName(source) || IsHexDigits(source))
        {
            value = default;
            return false;
        }

        return cache?.TryGet(source, out value) == true || TryParseHexOrName(source, cache, out value);
    }

    /// <summary>
    /// Converts a hexadecimal or named color through ImageSharp and stores the result in the cache.
    /// </summary>
    /// <param name="source">The validated color text.</param>
    /// <param name="cache">The cache to fill, or <see langword="null"/>.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the color.</param>
    /// <returns><see langword="true"/> if ImageSharp parsed the color; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseHexOrName(ReadOnlySpan<char> source, SvgColorCache? cache, out Color value)
    {
        string text = source.ToString();
        bool parsed = source[0] == '#'
            ? Color.TryParseHex(text, out value, ColorHexFormat.Rgba)
            : Color.TryParse(text.ToLowerInvariant(), out value, ColorHexFormat.Rgba);

        if (parsed)
        {
            cache?.Add(text, value);
        }

        return parsed;
    }

    /// <summary>
    /// Parses <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>, or <c>hsla()</c>.
    /// </summary>
    /// <param name="source">The trimmed value that ends with <c>)</c>.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the color.</param>
    /// <returns><see langword="true"/> if the value is a valid color function; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseFunction(ReadOnlySpan<char> source, out Color value)
    {
        value = default;

        // CSS function names are followed directly by "(", without whitespace.
        int open = source.IndexOf('(');
        if (open <= 0)
        {
            return false;
        }

        ReadOnlySpan<char> name = source[..open];
        bool isRgb = name.Equals("rgb", StringComparison.OrdinalIgnoreCase) || name.Equals("rgba", StringComparison.OrdinalIgnoreCase);
        bool isHsl = !isRgb && (name.Equals("hsl", StringComparison.OrdinalIgnoreCase) || name.Equals("hsla", StringComparison.OrdinalIgnoreCase));
        if (!isRgb && !isHsl)
        {
            return false;
        }

        ReadOnlySpan<char> arguments = source[(open + 1)..^1];
        Span<Component> components = stackalloc Component[4];
        if (!TrySplitArguments(arguments, components, out int count, out bool legacy))
        {
            return false;
        }

        // The alpha is a number or a percentage in both syntaxes. It defaults to opaque.
        float alpha = 1F;
        if (count == 4)
        {
            Component a = components[3];
            if (a.Unit is not (ComponentUnit.Number or ComponentUnit.Percent))
            {
                return false;
            }

            alpha = Math.Clamp(a.Unit == ComponentUnit.Percent ? a.Value / 100F : a.Value, 0F, 1F);
        }

        Vector4 rgba;
        if (isRgb)
        {
            // The comma syntax needs all three channels to be numbers or all to be percentages. The space syntax allows a mix.
            ComponentUnit first = components[0].Unit;
            for (int i = 0; i < 3; i++)
            {
                ComponentUnit unit = components[i].Unit;
                if (unit is not (ComponentUnit.Number or ComponentUnit.Percent) || (legacy && unit != first))
                {
                    return false;
                }
            }

            rgba = new Vector4(Channel(components[0]), Channel(components[1]), Channel(components[2]), alpha);
        }
        else
        {
            Component hue = components[0];
            if (hue.Unit is not (ComponentUnit.Number or ComponentUnit.Angle))
            {
                return false;
            }

            // The comma syntax needs percentages for saturation and lightness. The space syntax also allows numbers, which
            // mean percentages.
            Component saturation = components[1];
            Component lightness = components[2];
            if (!IsHslPercentage(saturation.Unit, legacy) || !IsHslPercentage(lightness.Unit, legacy))
            {
                return false;
            }

            rgba = HslToRgb(hue.Value, Math.Clamp(saturation.Value / 100F, 0F, 1F), Math.Clamp(lightness.Value / 100F, 0F, 1F), alpha);
        }

        value = Color.FromScaledVector(rgba);
        return true;
    }

    /// <summary>
    /// Returns whether a unit is valid for the saturation or lightness of <c>hsl()</c>.
    /// </summary>
    /// <param name="unit">The component unit.</param>
    /// <param name="legacy">Whether the function uses the comma syntax.</param>
    /// <returns><see langword="true"/> if the unit is valid; otherwise, <see langword="false"/>.</returns>
    private static bool IsHslPercentage(ComponentUnit unit, bool legacy)
        => unit == ComponentUnit.Percent || (!legacy && unit == ComponentUnit.Number);

    /// <summary>
    /// Converts an <c>rgb()</c> channel to the range 0 to 1. Numbers are in the range 0 to 255.
    /// </summary>
    /// <param name="component">The channel component.</param>
    /// <returns>The clamped channel value.</returns>
    private static float Channel(Component component)
        => Math.Clamp(component.Unit == ComponentUnit.Percent ? component.Value / 100F : component.Value / 255F, 0F, 1F);

    /// <summary>
    /// Converts HSL to RGB with the algorithm in CSS Color Level 4, section 7.1.
    /// </summary>
    /// <param name="hue">The hue in degrees.</param>
    /// <param name="saturation">The saturation in the range 0 to 1.</param>
    /// <param name="lightness">The lightness in the range 0 to 1.</param>
    /// <param name="alpha">The alpha in the range 0 to 1.</param>
    /// <returns>The color as scaled RGBA.</returns>
    private static Vector4 HslToRgb(float hue, float saturation, float lightness, float alpha)
    {
        hue %= 360F;
        if (hue < 0)
        {
            hue += 360F;
        }

        float a = saturation * MathF.Min(lightness, 1F - lightness);

        // f(n) = L - a * max(-1, min(k - 3, 9 - k, 1)) with k = (n + H / 30) mod 12, for n = 0, 8, and 4.
        float F(float n)
        {
            float k = (n + (hue / 30F)) % 12F;
            return lightness - (a * MathF.Max(-1F, MathF.Min(MathF.Min(k - 3F, 9F - k), 1F)));
        }

        return new Vector4(F(0F), F(8F), F(4F), alpha);
    }

    /// <summary>
    /// Splits color function arguments in either syntax. The comma syntax is <c>a, b, c[, alpha]</c>. The space syntax is
    /// <c>a b c[ / alpha]</c>.
    /// </summary>
    /// <param name="arguments">The characters between the parentheses.</param>
    /// <param name="components">The buffer that receives up to four components.</param>
    /// <param name="count">When this method returns <see langword="true"/>, contains 3 or 4.</param>
    /// <param name="legacy">When this method returns <see langword="true"/>, indicates whether the comma syntax was used.</param>
    /// <returns><see langword="true"/> if the arguments are well formed; otherwise, <see langword="false"/>.</returns>
    private static bool TrySplitArguments(ReadOnlySpan<char> arguments, Span<Component> components, out int count, out bool legacy)
    {
        count = 0;
        legacy = arguments.Contains(',');
        ReadOnlySpan<char> rest = SvgNumberParser.SkipWhitespace(arguments);
        while (!rest.IsEmpty)
        {
            if (count > 0)
            {
                // Read the separator before the next component.
                if (legacy)
                {
                    if (rest[0] != ',')
                    {
                        return false;
                    }

                    rest = SvgNumberParser.SkipWhitespace(rest[1..]);
                }
                else if (rest[0] == '/')
                {
                    // The slash introduces the alpha, which must be the fourth component.
                    if (count != 3)
                    {
                        return false;
                    }

                    rest = SvgNumberParser.SkipWhitespace(rest[1..]);
                }
                else if (count == 3)
                {
                    // In the space syntax the alpha needs the slash.
                    return false;
                }

                // A separator must be followed by a component.
                if (rest.IsEmpty)
                {
                    return false;
                }
            }

            if (count == components.Length || !TryReadComponent(rest, out components[count], out int length))
            {
                return false;
            }

            count++;
            ReadOnlySpan<char> after = rest[length..];
            rest = SvgNumberParser.SkipWhitespace(after);

            // In the space syntax, components need whitespace between them.
            if (!legacy && !rest.IsEmpty && rest[0] != '/' && after.Length == rest.Length)
            {
                return false;
            }
        }

        return count is 3 or 4;
    }

    /// <summary>
    /// Reads one color function component: a number, a percentage, or an angle.
    /// </summary>
    /// <param name="source">The characters to read.</param>
    /// <param name="component">When this method returns <see langword="true"/>, contains the component.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if a component was read; otherwise, <see langword="false"/>.</returns>
    private static bool TryReadComponent(ReadOnlySpan<char> source, out Component component, out int length)
    {
        component = default;
        if (!SvgNumberParser.TryReadNumber(source, false, out float number, out length))
        {
            return false;
        }

        ReadOnlySpan<char> rest = source[length..];
        if (!rest.IsEmpty && rest[0] == '%')
        {
            component = new Component(number, ComponentUnit.Percent);
            length++;
            return true;
        }

        int unitLength = 0;
        while (unitLength < rest.Length && char.IsAsciiLetter(rest[unitLength]))
        {
            unitLength++;
        }

        if (unitLength == 0)
        {
            component = new Component(number, ComponentUnit.Number);
            return true;
        }

        // CSS angle units. A full turn is 360deg, 400grad, 2*pi rad, or 1turn.
        ReadOnlySpan<char> unit = rest[..unitLength];
        float degrees;
        if (unit.Equals("deg", StringComparison.OrdinalIgnoreCase))
        {
            degrees = number;
        }
        else if (unit.Equals("grad", StringComparison.OrdinalIgnoreCase))
        {
            degrees = number * 0.9F;
        }
        else if (unit.Equals("rad", StringComparison.OrdinalIgnoreCase))
        {
            degrees = number * (180F / MathF.PI);
        }
        else if (unit.Equals("turn", StringComparison.OrdinalIgnoreCase))
        {
            degrees = number * 360F;
        }
        else
        {
            return false;
        }

        component = new Component(degrees, ComponentUnit.Angle);
        length += unitLength;
        return true;
    }

    /// <summary>
    /// Returns whether the characters after <c>#</c> form a 3, 4, 6, or 8 digit hexadecimal color.
    /// </summary>
    /// <param name="digits">The characters after the number sign.</param>
    /// <returns><see langword="true"/> if the digits are a valid hexadecimal color; otherwise, <see langword="false"/>.</returns>
    private static bool IsHexColor(ReadOnlySpan<char> digits) => digits.Length is 3 or 4 or 6 or 8 && IsHexDigits(digits);

    /// <summary>
    /// Returns whether every character is an ASCII hexadecimal digit.
    /// </summary>
    /// <param name="source">The characters to test.</param>
    /// <returns><see langword="true"/> if every character is a hexadecimal digit; otherwise, <see langword="false"/>.</returns>
    private static bool IsHexDigits(ReadOnlySpan<char> source) => !source.ContainsAnyExcept(HexDigits);

    /// <summary>
    /// Returns whether every character is an ASCII letter, which is the form of every CSS color name.
    /// </summary>
    /// <param name="source">The characters to test.</param>
    /// <returns><see langword="true"/> if every character is an ASCII letter; otherwise, <see langword="false"/>.</returns>
    private static bool IsName(ReadOnlySpan<char> source) => !source.ContainsAnyExcept(Letters);

    /// <summary>
    /// One argument of a color function.
    /// </summary>
    private readonly struct Component
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Component"/> struct.
        /// </summary>
        /// <param name="value">The number, percentage, or angle in degrees.</param>
        /// <param name="unit">The unit.</param>
        public Component(float value, ComponentUnit unit)
        {
            this.Value = value;
            this.Unit = unit;
        }

        /// <summary>
        /// Gets the number, percentage, or angle in degrees.
        /// </summary>
        public float Value { get; }

        /// <summary>
        /// Gets the unit.
        /// </summary>
        public ComponentUnit Unit { get; }
    }
}
