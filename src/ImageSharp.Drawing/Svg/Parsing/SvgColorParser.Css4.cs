// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The color functions of CSS Color Level 4 besides <c>rgb()</c> and <c>hsl()</c> (<c>hwb()</c>, <c>lab()</c>,
/// <c>lch()</c>, <c>oklab()</c>, <c>oklch()</c>, <c>color()</c>), and of CSS Color Level 5 (<c>color-mix()</c>,
/// relative colors, <c>light-dark()</c>), with <c>none</c> and math functions in the components. The Chromium oracle
/// accepts all of them in <c>fill</c>, as an attribute and in CSS.
/// </content>
/// <remarks>
/// <para>
/// The colors keep their own space (<see cref="SvgCssColor"/>) until the outermost function ends, so a color outside the
/// sRGB gamut keeps its components through <c>color-mix()</c> and relative colors; the result is clipped to sRGB once.
/// </para>
/// <para>
/// Not supported: <c>currentColor</c> inside <c>color-mix()</c> and as the origin of a relative color, which would need the
/// color to stay unresolved until the element that paints is known (the declaration is invalid instead; the Chromium oracle
/// renders it), and system colors. <c>light-dark()</c> gives its first color: the oracle renders an SVG document as an
/// image with the light color scheme.
/// </para>
/// </remarks>
internal static partial class SvgColorParser
{
    /// <summary>
    /// The deepest nesting of colors inside color functions, such as <c>color-mix()</c> of <c>color-mix()</c>. Deeper
    /// nesting makes the value invalid, which bounds the recursion of a hostile value.
    /// </summary>
    internal const int MaxNesting = 16;

    /// <summary>
    /// Returns whether the arguments of a color function use the relative color syntax: they start with <c>from</c>.
    /// </summary>
    /// <param name="arguments">The characters between the parentheses.</param>
    /// <returns><see langword="true"/> for a relative color.</returns>
    private static bool IsRelative(ReadOnlySpan<char> arguments)
    {
        ReadOnlySpan<char> rest = SvgNumberParser.SkipWhitespace(arguments);
        return rest.Length > 4 && rest[..4].Equals("from", StringComparison.OrdinalIgnoreCase) && SvgNumberParser.IsWhitespace(rest[4]);
    }

    /// <summary>
    /// Returns whether a value has a comma outside parentheses.
    /// </summary>
    /// <param name="source">The value.</param>
    /// <returns><see langword="true"/> if a comma separates top-level arguments.</returns>
    private static bool HasTopLevelComma(ReadOnlySpan<char> source)
    {
        int nesting = 0;
        foreach (char c in source)
        {
            if (c == '(')
            {
                nesting++;
            }
            else if (c == ')')
            {
                nesting--;
            }
            else if (c == ',' && nesting == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads one color function component: a number, a percentage, an angle, <c>none</c>, a math function of one of these
    /// types, or a channel keyword of a relative color.
    /// </summary>
    /// <param name="source">The characters to read.</param>
    /// <param name="variables">The channel keywords of a relative color, or none.</param>
    /// <param name="component">When this method returns <see langword="true"/>, contains the component.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if a component was read; otherwise, <see langword="false"/>.</returns>
    private static bool TryReadComponent(ReadOnlySpan<char> source, in SvgCalcVariables variables, out Component component, out int length)
    {
        component = default;
        length = 0;
        if (source.IsEmpty)
        {
            return false;
        }

        if (char.IsAsciiLetter(source[0]))
        {
            if (SvgCalc.IsMathFunction(source))
            {
                if (!SvgCalc.TryRead(source, in variables, out SvgCalcResult result, out length))
                {
                    return false;
                }

                ComponentUnit unit;
                switch (result.Kind)
                {
                    case SvgCalcKind.Number:
                        unit = ComponentUnit.Number;
                        break;
                    case SvgCalcKind.Percent:
                        unit = ComponentUnit.Percent;
                        break;
                    case SvgCalcKind.Angle:
                        unit = ComponentUnit.Angle;
                        break;
                    default:
                        return false;
                }

                component = new Component((float)result.Value, unit);
                return float.IsFinite(component.Value);
            }

            int end = 0;
            while (end < source.Length && (char.IsAsciiLetterOrDigit(source[end]) || source[end] == '-'))
            {
                end++;
            }

            ReadOnlySpan<char> word = source[..end];
            length = end;
            if (word.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                component = new Component(0F, ComponentUnit.None);
                return true;
            }

            if (variables.TryGetValue(word, out double channel))
            {
                component = new Component((float)channel, ComponentUnit.Number);
                return true;
            }

            return false;
        }

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
        if (!SvgNumberParser.TryReadAngle(source, false, out float degrees, out int angleLength))
        {
            return false;
        }

        component = new Component(degrees, ComponentUnit.Angle);
        length = angleLength;
        return true;
    }

    /// <summary>
    /// Parses a color function other than the plain <c>rgb()</c> and <c>hsl()</c>.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The characters between the parentheses.</param>
    /// <param name="depth">The nesting depth.</param>
    /// <param name="color">When this method returns <see langword="true"/>, contains the color.</param>
    /// <returns><see langword="true"/> if the function is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseCssFunction(ReadOnlySpan<char> name, ReadOnlySpan<char> arguments, int depth, out SvgCssColor color)
    {
        color = default;
        if (depth > MaxNesting)
        {
            return false;
        }

        if (name.Equals("color-mix", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseColorMix(arguments, depth, out color);
        }

        if (name.Equals("light-dark", StringComparison.OrdinalIgnoreCase))
        {
            // The light color scheme applies: the first color.
            ReadOnlySpan<char> rest = arguments;
            return TryTakeArgument(ref rest, out ReadOnlySpan<char> light)
                && TryTakeArgument(ref rest, out ReadOnlySpan<char> dark)
                && rest.IsEmpty
                && TryParseNestedColor(dark, depth + 1, out _)
                && TryParseNestedColor(light, depth + 1, out color);
        }

        bool isColor = name.Equals("color", StringComparison.OrdinalIgnoreCase);
        SvgColorSpace space = default;
        if (!isColor && !TryGetFunctionSpace(name, out space))
        {
            return false;
        }

        // Relative color: from <color>, then the components with the channels of the origin as keywords.
        ReadOnlySpan<char> components = arguments;
        SvgCssColor origin = default;
        bool relative = IsRelative(arguments);
        if (relative)
        {
            ReadOnlySpan<char> rest = SvgNumberParser.SkipWhitespace(SvgNumberParser.SkipWhitespace(arguments)[4..]);
            int end = GetTokenEnd(rest);
            if (end <= 0 || !TryParseNestedColor(rest[..end], depth + 1, out origin))
            {
                return false;
            }

            components = SvgNumberParser.SkipWhitespace(rest[end..]);
        }

        if (isColor)
        {
            // color( <space> <components> ): the space keyword comes first, after the origin of a relative color.
            int end = 0;
            while (end < components.Length && (char.IsAsciiLetterOrDigit(components[end]) || components[end] == '-'))
            {
                end++;
            }

            if (!TryGetPredefinedSpace(components[..end], out space) || end == components.Length || !SvgNumberParser.IsWhitespace(components[end]))
            {
                return false;
            }

            components = components[end..];
        }

        // These functions have no comma syntax.
        SvgCalcVariables variables = relative ? GetChannelKeywords(origin.ConvertTo(space), isColor) : default;
        Span<Component> values = stackalloc Component[4];
        if (!TrySplitArguments(components, values, in variables, out int count, out bool legacy) || legacy)
        {
            return false;
        }

        // The alpha of a relative color defaults to the alpha of the origin.
        double alpha = relative ? origin.Alpha : 1D;
        int missing = 0;
        if (count == 4)
        {
            Component a = values[3];
            if (a.Unit is not (ComponentUnit.Number or ComponentUnit.Percent or ComponentUnit.None))
            {
                return false;
            }

            alpha = Math.Clamp(a.Unit == ComponentUnit.Percent ? a.Value / 100D : a.Value, 0D, 1D);
            missing |= a.Unit == ComponentUnit.None ? 8 : 0;
        }

        Span<double> resolved = stackalloc double[3];
        for (int i = 0; i < 3; i++)
        {
            if (!TryResolveComponent(space, i, values[i], isColor, out resolved[i]))
            {
                return false;
            }

            missing |= values[i].Unit == ComponentUnit.None ? 1 << i : 0;
        }

        color = new SvgCssColor(space, resolved[0], resolved[1], resolved[2], alpha, missing);
        return true;
    }

    /// <summary>
    /// Maps a color function name to its space.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="space">When this method returns <see langword="true"/>, contains the space.</param>
    /// <returns><see langword="true"/> for <c>rgb</c>, <c>rgba</c>, <c>hsl</c>, <c>hsla</c>, <c>hwb</c>, <c>lab</c>, <c>lch</c>, <c>oklab</c>, or <c>oklch</c>.</returns>
    private static bool TryGetFunctionSpace(ReadOnlySpan<char> name, out SvgColorSpace space)
    {
        Span<char> lower = stackalloc char[8];
        space = default;
        if (name.Length > lower.Length)
        {
            return false;
        }

        int length = name.ToLowerInvariant(lower);
        switch (lower[..length])
        {
            case "rgb":
            case "rgba":
                space = SvgColorSpace.Srgb;
                return true;
            case "hsl":
            case "hsla":
                space = SvgColorSpace.Hsl;
                return true;
            case "hwb":
                space = SvgColorSpace.Hwb;
                return true;
            case "lab":
                space = SvgColorSpace.Lab;
                return true;
            case "lch":
                space = SvgColorSpace.Lch;
                return true;
            case "oklab":
                space = SvgColorSpace.Oklab;
                return true;
            case "oklch":
                space = SvgColorSpace.Oklch;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Maps a predefined color space keyword of <c>color()</c> (CSS Color 4, section 10) to its space.
    /// </summary>
    /// <param name="name">The keyword.</param>
    /// <param name="space">When this method returns <see langword="true"/>, contains the space.</param>
    /// <returns><see langword="true"/> if the keyword names a predefined space.</returns>
    private static bool TryGetPredefinedSpace(ReadOnlySpan<char> name, out SvgColorSpace space)
    {
        Span<char> lower = stackalloc char[24];
        space = default;
        if (name.Length > lower.Length)
        {
            return false;
        }

        int length = name.ToLowerInvariant(lower);
        switch (lower[..length])
        {
            case "srgb":
                space = SvgColorSpace.Srgb;
                return true;
            case "srgb-linear":
                space = SvgColorSpace.SrgbLinear;
                return true;
            case "display-p3":
                space = SvgColorSpace.DisplayP3;
                return true;
            case "display-p3-linear":
                space = SvgColorSpace.DisplayP3Linear;
                return true;
            case "a98-rgb":
                space = SvgColorSpace.A98Rgb;
                return true;
            case "prophoto-rgb":
                space = SvgColorSpace.ProphotoRgb;
                return true;
            case "rec2020":
                space = SvgColorSpace.Rec2020;
                return true;
            case "xyz-d50":
                space = SvgColorSpace.XyzD50;
                return true;
            case "xyz":
            case "xyz-d65":
                space = SvgColorSpace.XyzD65;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Converts a component to the value of its space, with the percentage reference ranges and the clamping at
    /// parsed-value time of CSS Color 4: sections 5.1 (<c>rgb()</c>), 7 (<c>hsl()</c>), 8 (<c>hwb()</c>), 9.3 and 9.4
    /// (Lab, LCH, Oklab, OkLCh), and 10.1 (<c>color()</c>).
    /// </summary>
    /// <param name="space">The space.</param>
    /// <param name="index">The index of the component.</param>
    /// <param name="component">The component.</param>
    /// <param name="isColor">Whether the function is <c>color()</c>, whose components are 0 to 1.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value; zero for <c>none</c>.</param>
    /// <returns><see langword="true"/> if the component has a valid type for its place; otherwise, <see langword="false"/>.</returns>
    private static bool TryResolveComponent(SvgColorSpace space, int index, Component component, bool isColor, out double value)
    {
        value = component.Value;
        if (component.Unit == ComponentUnit.None)
        {
            value = 0D;
            return true;
        }

        // A hue takes a number or an angle; every other component a number or a percentage.
        bool isHue = SvgCssColor.GetHueIndex(space) == index;
        if (isHue ? component.Unit == ComponentUnit.Percent : component.Unit == ComponentUnit.Angle)
        {
            return false;
        }

        bool percent = component.Unit == ComponentUnit.Percent;
        if (isColor)
        {
            value = percent ? value / 100D : value;
            return true;
        }

        switch (space)
        {
            case SvgColorSpace.Srgb:
                // rgb(): numbers are 0 to 255, clamped at parsed-value time.
                value = Math.Clamp(percent ? value / 100D : value / 255D, 0D, 1D);
                return true;
            case SvgColorSpace.Hsl:
            case SvgColorSpace.Hwb:
                // A negative saturation is clamped to zero.
                value = index == 1 && space == SvgColorSpace.Hsl ? Math.Max(value, 0D) : value;
                return true;
            case SvgColorSpace.Lab:
                value = index == 0 ? Math.Clamp(value, 0D, 100D) : percent ? value * 1.25D : value;
                return true;
            case SvgColorSpace.Lch:
                value = index switch
                {
                    0 => Math.Clamp(value, 0D, 100D),
                    1 => Math.Max(percent ? value * 1.5D : value, 0D),
                    _ => value
                };

                return true;
            case SvgColorSpace.Oklab:
                value = index == 0 ? Math.Clamp(percent ? value / 100D : value, 0D, 1D) : percent ? value * 0.004D : value;
                return true;
            default:
                value = index switch
                {
                    0 => Math.Clamp(percent ? value / 100D : value, 0D, 1D),
                    1 => Math.Max(percent ? value * 0.004D : value, 0D),
                    _ => value
                };

                return true;
        }
    }

    /// <summary>
    /// Returns the channel keywords of a relative color: the components of the origin in the space of the function, in
    /// the units that the function takes them in (CSS Color 5, section 4).
    /// </summary>
    /// <param name="origin">The origin color, converted to the space of the function.</param>
    /// <param name="isColor">Whether the function is <c>color()</c>.</param>
    /// <returns>The keywords.</returns>
    private static SvgCalcVariables GetChannelKeywords(SvgCssColor origin, bool isColor)
    {
        // A missing component of the origin counts as zero.
        double c0 = (origin.Missing & 1) != 0 ? 0D : origin.C0;
        double c1 = (origin.Missing & 2) != 0 ? 0D : origin.C1;
        double c2 = (origin.Missing & 4) != 0 ? 0D : origin.C2;
        string[] names;
        if (isColor)
        {
            names = origin.Space is SvgColorSpace.XyzD50 or SvgColorSpace.XyzD65 ? ["x", "y", "z", "alpha"] : ["r", "g", "b", "alpha"];
        }
        else
        {
            names = origin.Space switch
            {
                SvgColorSpace.Srgb => ["r", "g", "b", "alpha"],
                SvgColorSpace.Hsl => ["h", "s", "l", "alpha"],
                SvgColorSpace.Hwb => ["h", "w", "b", "alpha"],
                SvgColorSpace.Lab or SvgColorSpace.Oklab => ["l", "a", "b", "alpha"],
                _ => ["l", "c", "h", "alpha"],
            };

            // rgb() takes its channels as numbers from 0 to 255.
            if (origin.Space == SvgColorSpace.Srgb)
            {
                c0 *= 255D;
                c1 *= 255D;
                c2 *= 255D;
            }
        }

        return new SvgCalcVariables(names, [c0, c1, c2, origin.Alpha]);
    }

    /// <summary>
    /// Parses <c>color-mix(&lt;color-interpolation-method&gt;?, &lt;color&gt; &lt;percentage&gt;?, &lt;color&gt;
    /// &lt;percentage&gt;?)</c> of CSS Color Level 5, section 3.
    /// </summary>
    /// <remarks>
    /// The percentages normalize as CSS Values 5 says: a missing one is 100% minus the other, both missing are 50% each,
    /// and a sum other than 100% scales both to 100%, with an alpha multiplier of the sum when it is less. Interpolation
    /// follows CSS Color 4, section 13: missing components take the value of the other color, the components other than
    /// the hue are premultiplied by alpha, and the hue follows the hue interpolation method (section 13.6, shorter by
    /// default).
    /// </remarks>
    /// <param name="arguments">The characters between the parentheses.</param>
    /// <param name="depth">The nesting depth.</param>
    /// <param name="color">When this method returns <see langword="true"/>, contains the mixed color.</param>
    /// <returns><see langword="true"/> if the function is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseColorMix(ReadOnlySpan<char> arguments, int depth, out SvgCssColor color)
    {
        color = default;
        ReadOnlySpan<char> rest = arguments;
        if (!TryTakeArgument(ref rest, out ReadOnlySpan<char> first) || !TryTakeArgument(ref rest, out ReadOnlySpan<char> second))
        {
            return false;
        }

        // The interpolation method is optional and defaults to Oklab (CSS Color 5, section 3.1). The Chromium oracle mixes
        // exactly two colors; a list of one or of three colors is invalid there, and here.
        SvgColorSpace space = SvgColorSpace.Oklab;
        int hueMethod = 0;
        if (TryParseInterpolationMethod(first, out SvgColorSpace method, out int methodHue))
        {
            space = method;
            hueMethod = methodHue;
            first = second;
            if (!TryTakeArgument(ref rest, out second))
            {
                return false;
            }
        }

        if (!rest.IsEmpty
            || !TryParseMixPart(first, depth, out SvgCssColor a, out double p1)
            || !TryParseMixPart(second, depth, out SvgCssColor b, out double p2))
        {
            return false;
        }

        // CSS Values 5, section 6.1, "normalize mix percentages" with forced normalization: omitted percentages share what
        // the specified ones leave of 100%, a total above 0% scales to 100%, and what the total lacks of 100% becomes the
        // alpha multiplier. Two 0% items mix half and half into a transparent color, as the Chromium oracle renders.
        bool omitted1 = double.IsNaN(p1);
        bool omitted2 = double.IsNaN(p2);
        double specified = Math.Min((omitted1 ? 0D : p1) + (omitted2 ? 0D : p2), 100D);
        int omittedCount = (omitted1 ? 1 : 0) + (omitted2 ? 1 : 0);
        p1 = omitted1 ? (100D - specified) / omittedCount : p1;
        p2 = omitted2 ? (100D - specified) / omittedCount : p2;
        double sum = p1 + p2;
        double multiplier = sum < 100D ? sum / 100D : 1D;
        double t = sum > 0D ? p2 / sum : 0.5D;

        SvgCssColor x = a.ConvertTo(space);
        SvgCssColor y = b.ConvertTo(space);
        int hueIndex = SvgCssColor.GetHueIndex(space);
        Span<double> u = stackalloc double[4];
        Span<double> v = stackalloc double[4];
        int missing = 0;
        for (int i = 0; i < 4; i++)
        {
            // Section 13.4: a missing component takes the value of the other color; missing in both stays missing.
            int bit = 1 << i;
            bool mx = (x.Missing & bit) != 0;
            bool my = (y.Missing & bit) != 0;
            u[i] = mx ? (my ? 0D : y.Get(i)) : x.Get(i);
            v[i] = my ? u[i] : y.Get(i);
            missing |= mx && my ? bit : 0;
        }

        // Section 13.6: adjust the hues for the interpolation method.
        if (hueIndex >= 0)
        {
            double h1 = Normalize(u[hueIndex]);
            double h2 = Normalize(v[hueIndex]);
            double delta = h2 - h1;
            switch (hueMethod)
            {
                case 1:
                    // longer
                    if (delta > 0D && delta < 180D)
                    {
                        h1 += 360D;
                    }
                    else if (delta > -180D && delta <= 0D)
                    {
                        h2 += 360D;
                    }

                    break;
                case 2:
                    // increasing
                    if (delta < 0D)
                    {
                        h2 += 360D;
                    }

                    break;
                case 3:
                    // decreasing
                    if (delta > 0D)
                    {
                        h1 += 360D;
                    }

                    break;
                default:
                    // shorter
                    if (delta > 180D)
                    {
                        h1 += 360D;
                    }
                    else if (delta < -180D)
                    {
                        h2 += 360D;
                    }

                    break;
            }

            u[hueIndex] = h1;
            v[hueIndex] = h2;
        }

        // Section 13.5: premultiply every component but the hue, interpolate, and divide by the interpolated alpha.
        double alpha = (u[3] * (1D - t)) + (v[3] * t);
        Span<double> result = stackalloc double[3];
        for (int i = 0; i < 3; i++)
        {
            if (i == hueIndex)
            {
                result[i] = Normalize((u[i] * (1D - t)) + (v[i] * t));
                continue;
            }

            double mixed = (u[i] * u[3] * (1D - t)) + (v[i] * v[3] * t);
            result[i] = alpha > 0D ? mixed / alpha : (u[i] * (1D - t)) + (v[i] * t);
        }

        color = new SvgCssColor(space, result[0], result[1], result[2], alpha * multiplier, missing);
        return true;

        static double Normalize(double hue)
        {
            hue %= 360D;
            return hue < 0D ? hue + 360D : hue;
        }
    }

    /// <summary>
    /// Parses the <c>&lt;color-interpolation-method&gt;</c> of <c>color-mix()</c>: <c>in</c>, a space, and for a
    /// cylindrical space an optional <c>shorter</c>, <c>longer</c>, <c>increasing</c>, or <c>decreasing</c> with
    /// <c>hue</c>.
    /// </summary>
    /// <param name="source">The first argument.</param>
    /// <param name="space">When this method returns <see langword="true"/>, contains the interpolation space.</param>
    /// <param name="hueMethod">When this method returns <see langword="true"/>, contains 0 shorter, 1 longer, 2 increasing, or 3 decreasing.</param>
    /// <returns><see langword="true"/> if the method is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseInterpolationMethod(ReadOnlySpan<char> source, out SvgColorSpace space, out int hueMethod)
    {
        space = default;
        hueMethod = 0;
        Span<Range> words = stackalloc Range[5];
        int count = SplitWords(source, words);
        if (count < 2 || !source[words[0]].Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> name = source[words[1]];
        if (!TryGetPredefinedSpace(name, out space))
        {
            if (name.Equals("hsl", StringComparison.OrdinalIgnoreCase))
            {
                space = SvgColorSpace.Hsl;
            }
            else if (name.Equals("hwb", StringComparison.OrdinalIgnoreCase))
            {
                space = SvgColorSpace.Hwb;
            }
            else if (name.Equals("lab", StringComparison.OrdinalIgnoreCase))
            {
                space = SvgColorSpace.Lab;
            }
            else if (name.Equals("lch", StringComparison.OrdinalIgnoreCase))
            {
                space = SvgColorSpace.Lch;
            }
            else if (name.Equals("oklab", StringComparison.OrdinalIgnoreCase))
            {
                space = SvgColorSpace.Oklab;
            }
            else if (name.Equals("oklch", StringComparison.OrdinalIgnoreCase))
            {
                space = SvgColorSpace.Oklch;
            }
            else
            {
                return false;
            }
        }

        if (count == 2)
        {
            return true;
        }

        if (count != 4 || SvgCssColor.GetHueIndex(space) < 0 || !source[words[3]].Equals("hue", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ReadOnlySpan<char> method = source[words[2]];
        hueMethod = method switch
        {
            _ when method.Equals("shorter", StringComparison.OrdinalIgnoreCase) => 0,
            _ when method.Equals("longer", StringComparison.OrdinalIgnoreCase) => 1,
            _ when method.Equals("increasing", StringComparison.OrdinalIgnoreCase) => 2,
            _ when method.Equals("decreasing", StringComparison.OrdinalIgnoreCase) => 3,
            _ => -1
        };

        return hueMethod >= 0;
    }

    /// <summary>
    /// Parses one color argument of <c>color-mix()</c>: a color and an optional percentage, in either order.
    /// </summary>
    /// <param name="source">The argument.</param>
    /// <param name="depth">The nesting depth of the enclosing function.</param>
    /// <param name="color">When this method returns <see langword="true"/>, contains the color.</param>
    /// <param name="percentage">When this method returns <see langword="true"/>, contains the percentage from 0 to 100, or NaN when it is omitted.</param>
    /// <returns><see langword="true"/> if the argument is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseMixPart(ReadOnlySpan<char> source, int depth, out SvgCssColor color, out double percentage)
    {
        color = default;
        percentage = double.NaN;
        source = SvgNumberParser.Trim(source);
        if (source.IsEmpty)
        {
            return false;
        }

        // The percentage is the first or the last word; the color is the rest.
        int firstEnd = GetTokenEnd(source);
        int lastStart = source.LastIndexOfAny(' ', '\t', '\n') + 1;
        ReadOnlySpan<char> colorText = source;
        if (firstEnd > 0 && firstEnd < source.Length && TryParsePercentage(source[..firstEnd], out double first))
        {
            percentage = first;
            colorText = source[firstEnd..];
        }
        else if (lastStart > 0 && source[^1] == '%' && TryParsePercentage(source[lastStart..], out double last))
        {
            percentage = last;
            colorText = source[..lastStart];
        }

        return percentage is not (< 0D or > 100D) && TryParseNestedColor(colorText, depth + 1, out color);

        static bool TryParsePercentage(ReadOnlySpan<char> text, out double value)
        {
            value = 0D;
            if (text.Length < 2 || text[^1] != '%' || !SvgNumberParser.TryParseNumber(text[..^1], out float number))
            {
                return false;
            }

            value = number;
            return true;
        }
    }

    /// <summary>
    /// Parses a color inside another color function, keeping its own space.
    /// </summary>
    /// <param name="source">The color.</param>
    /// <param name="depth">The nesting depth of the color.</param>
    /// <param name="color">When this method returns <see langword="true"/>, contains the color.</param>
    /// <returns><see langword="true"/> if the color is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseNestedColor(ReadOnlySpan<char> source, int depth, out SvgCssColor color)
    {
        color = default;
        source = SvgNumberParser.Trim(source);
        if (source.IsEmpty || depth > MaxNesting)
        {
            return false;
        }

        if (source[^1] == ')')
        {
            int open = source.IndexOf('(');
            if (open <= 0)
            {
                return false;
            }

            ReadOnlySpan<char> name = source[..open];
            ReadOnlySpan<char> arguments = source[(open + 1)..^1];
            bool isRgbOrHsl = name.Equals("rgb", StringComparison.OrdinalIgnoreCase)
                || name.Equals("rgba", StringComparison.OrdinalIgnoreCase)
                || name.Equals("hsl", StringComparison.OrdinalIgnoreCase)
                || name.Equals("hsla", StringComparison.OrdinalIgnoreCase);

            if (!isRgbOrHsl || IsRelative(arguments))
            {
                return TryParseCssFunction(name, arguments, depth, out color);
            }
        }

        if (!TryParse(source, null, out Color value))
        {
            return false;
        }

        color = SvgCssColor.FromColor(value);
        return true;
    }

    /// <summary>
    /// Takes the next comma-separated argument, outside parentheses.
    /// </summary>
    /// <param name="rest">The remaining arguments, which the method advances past the comma.</param>
    /// <param name="argument">When this method returns <see langword="true"/>, contains the argument, trimmed.</param>
    /// <returns><see langword="true"/> if a non-empty argument was taken; otherwise, <see langword="false"/>.</returns>
    private static bool TryTakeArgument(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> argument)
    {
        int nesting = 0;
        int end = rest.Length;
        for (int i = 0; i < rest.Length; i++)
        {
            char c = rest[i];
            if (c == '(')
            {
                nesting++;
            }
            else if (c == ')')
            {
                nesting--;
            }
            else if (c == ',' && nesting == 0)
            {
                end = i;
                break;
            }
        }

        argument = SvgNumberParser.Trim(rest[..end]);
        rest = end < rest.Length ? rest[(end + 1)..] : [];

        // A trailing comma leaves an empty argument, which is invalid.
        return !argument.IsEmpty;
    }

    /// <summary>
    /// Returns the end of the first token of a value: a function through its closing parenthesis, or a run of characters
    /// up to white space.
    /// </summary>
    /// <param name="source">The value, without leading white space.</param>
    /// <returns>The length of the token, or -1 when a parenthesis is not closed.</returns>
    private static int GetTokenEnd(ReadOnlySpan<char> source)
    {
        int nesting = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '(')
            {
                nesting++;
            }
            else if (c == ')')
            {
                if (--nesting == 0)
                {
                    return i + 1;
                }
            }
            else if (nesting == 0 && SvgNumberParser.IsWhitespace(c))
            {
                return i;
            }
        }

        return nesting == 0 ? source.Length : -1;
    }

    /// <summary>
    /// Splits a value into words separated by white space.
    /// </summary>
    /// <param name="source">The value.</param>
    /// <param name="words">The buffer that receives the ranges of the words.</param>
    /// <returns>The number of words, or more than the buffer holds when it overflows.</returns>
    private static int SplitWords(ReadOnlySpan<char> source, Span<Range> words)
    {
        int count = 0;
        int i = 0;
        while (i < source.Length)
        {
            while (i < source.Length && SvgNumberParser.IsWhitespace(source[i]))
            {
                i++;
            }

            if (i == source.Length)
            {
                break;
            }

            int start = i;
            while (i < source.Length && !SvgNumberParser.IsWhitespace(source[i]))
            {
                i++;
            }

            if (count == words.Length)
            {
                return count + 1;
            }

            words[count++] = new Range(start, i);
        }

        return count;
    }
}
