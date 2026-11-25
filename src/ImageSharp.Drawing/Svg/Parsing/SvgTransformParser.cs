// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses an SVG transform list, as used by <c>transform</c> and <c>gradientTransform</c>, into one matrix.
/// </summary>
internal static class SvgTransformParser
{
    /// <summary>
    /// The two-dimensional CSS transform functions.
    /// </summary>
    private enum CssFunction
    {
        /// <summary>
        /// A name that is not a two-dimensional CSS transform function.
        /// </summary>
        Unknown,

        /// <summary>
        /// <c>matrix(a, b, c, d, e, f)</c>.
        /// </summary>
        Matrix,

        /// <summary>
        /// <c>translate(tx [, ty])</c>.
        /// </summary>
        Translate,

        /// <summary>
        /// <c>translateX(tx)</c>.
        /// </summary>
        TranslateX,

        /// <summary>
        /// <c>translateY(ty)</c>.
        /// </summary>
        TranslateY,

        /// <summary>
        /// <c>scale(sx [, sy])</c>.
        /// </summary>
        Scale,

        /// <summary>
        /// <c>scaleX(sx)</c>.
        /// </summary>
        ScaleX,

        /// <summary>
        /// <c>scaleY(sy)</c>.
        /// </summary>
        ScaleY,

        /// <summary>
        /// <c>rotate(angle)</c>.
        /// </summary>
        Rotate,

        /// <summary>
        /// <c>skew(ax [, ay])</c>.
        /// </summary>
        Skew,

        /// <summary>
        /// <c>skewX(angle)</c>.
        /// </summary>
        SkewX,

        /// <summary>
        /// <c>skewY(angle)</c>.
        /// </summary>
        SkewY
    }

    /// <summary>
    /// Parses a transform list: <c>matrix</c>, <c>translate</c>, <c>scale</c>, <c>rotate</c>, <c>skewX</c>, and
    /// <c>skewY</c> in any number, separated by whitespace or commas. Function names are case-sensitive, as in the SVG 1.1
    /// grammar. An empty list is the identity.
    /// </summary>
    /// <remarks>
    /// The first transform in the list is the outermost, so <c>translate(10) scale(2)</c> scales a point first and then
    /// translates it. <see cref="Matrix3x2"/> uses row vectors (<c>p' = p * M</c>), so each new transform multiplies the
    /// result from the left: <c>M = M_last * ... * M_first</c>.
    /// </remarks>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the combined transform.</param>
    /// <returns><see langword="true"/> if the whole list is valid and finite; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> source, out Matrix3x2 value)
    {
        value = Matrix3x2.Identity;
        Span<float> arguments = stackalloc float[6];
        source = SvgNumberParser.SkipWhitespace(source);
        bool first = true;
        while (!source.IsEmpty)
        {
            if (!first && !SvgNumberParser.TrySkipCommaWhitespace(ref source))
            {
                return false;
            }

            first = false;
            int nameLength = 0;
            while (nameLength < source.Length && char.IsAsciiLetter(source[nameLength]))
            {
                nameLength++;
            }

            ReadOnlySpan<char> name = source[..nameLength];
            source = SvgNumberParser.SkipWhitespace(source[nameLength..]);
            if (source.IsEmpty || source[0] != '(')
            {
                return false;
            }

            source = SvgNumberParser.SkipWhitespace(source[1..]);
            int count = 0;
            while (!source.IsEmpty && source[0] != ')')
            {
                if (count > 0 && !SvgNumberParser.TrySkipCommaWhitespace(ref source))
                {
                    return false;
                }

                if (count == arguments.Length || !SvgNumberParser.TryReadNumber(source, false, out arguments[count], out int length))
                {
                    return false;
                }

                count++;
                source = SvgNumberParser.SkipWhitespace(source[length..]);
            }

            if (source.IsEmpty || !TryCreate(name, arguments[..count], out Matrix3x2 transform))
            {
                return false;
            }

            source = SvgNumberParser.SkipWhitespace(source[1..]);
            value = transform * value;
        }

        return IsFinite(value);
    }

    /// <summary>
    /// Parses the value of the CSS <c>transform</c> property: <c>none</c>, or a list of two-dimensional CSS transform
    /// functions, as CSS Transforms Level 1 defines them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The CSS grammar differs from the SVG attribute grammar: function names are ASCII case-insensitive, arguments are
    /// separated by commas, angles need a unit (<c>deg</c>, <c>grad</c>, <c>rad</c>, or <c>turn</c>) unless they are zero,
    /// and translations need a length unit unless they are zero. The functions are <c>matrix</c>, <c>translate</c>,
    /// <c>translateX</c>, <c>translateY</c>, <c>scale</c>, <c>scaleX</c>, <c>scaleY</c>, <c>rotate</c>, <c>skew</c>,
    /// <c>skewX</c>, and <c>skewY</c>. A scale factor can be a percentage, as CSS Transforms Level 2 allows.
    /// </para>
    /// <para>
    /// Translations accept <c>px</c> only. Other units would need the resolution or the font size, which the parser does
    /// not have, and a percentage refers to the reference box; such a value makes the declaration invalid. Three-dimensional
    /// functions are invalid too. The order of composition is the same as for <see cref="TryParse"/>.
    /// </para>
    /// </remarks>
    /// <param name="source">The characters to parse.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the combined transform.</param>
    /// <returns><see langword="true"/> if the value is valid and finite; otherwise, <see langword="false"/>.</returns>
    public static bool TryParseCss(ReadOnlySpan<char> source, out Matrix3x2 value)
    {
        value = Matrix3x2.Identity;
        source = SvgNumberParser.Trim(source);
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Span<float> arguments = stackalloc float[6];
        if (source.IsEmpty)
        {
            return false;
        }

        while (!source.IsEmpty)
        {
            int nameLength = 0;
            while (nameLength < source.Length && char.IsAsciiLetter(source[nameLength]))
            {
                nameLength++;
            }

            // A CSS function token has its parenthesis directly after the name.
            if (nameLength == 0 || nameLength == source.Length || source[nameLength] != '(')
            {
                return false;
            }

            CssFunction function = GetCssFunction(source[..nameLength]);
            if (function == CssFunction.Unknown)
            {
                return false;
            }

            source = SvgNumberParser.SkipWhitespace(source[(nameLength + 1)..]);
            int count = 0;
            while (!source.IsEmpty && source[0] != ')')
            {
                if (count > 0)
                {
                    if (source[0] != ',')
                    {
                        return false;
                    }

                    source = SvgNumberParser.SkipWhitespace(source[1..]);
                }

                if (count == arguments.Length || !TryReadCssArgument(source, function, out arguments[count], out int length))
                {
                    return false;
                }

                count++;
                source = SvgNumberParser.SkipWhitespace(source[length..]);
            }

            if (source.IsEmpty || !TryCreateCss(function, arguments[..count], out Matrix3x2 transform))
            {
                return false;
            }

            source = SvgNumberParser.SkipWhitespace(source[1..]);
            value = transform * value;
        }

        return IsFinite(value);
    }

    /// <summary>
    /// Maps a CSS transform function name to its function, ignoring ASCII case.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <returns>The function, or <see cref="CssFunction.Unknown"/>.</returns>
    private static CssFunction GetCssFunction(ReadOnlySpan<char> name)
    {
        Span<char> lower = stackalloc char[16];
        if (name.Length > lower.Length)
        {
            return CssFunction.Unknown;
        }

        int length = name.ToLowerInvariant(lower);
        return lower[..length] switch
        {
            "matrix" => CssFunction.Matrix,
            "translate" => CssFunction.Translate,
            "translatex" => CssFunction.TranslateX,
            "translatey" => CssFunction.TranslateY,
            "scale" => CssFunction.Scale,
            "scalex" => CssFunction.ScaleX,
            "scaley" => CssFunction.ScaleY,
            "rotate" => CssFunction.Rotate,
            "skew" => CssFunction.Skew,
            "skewx" => CssFunction.SkewX,
            "skewy" => CssFunction.SkewY,
            _ => CssFunction.Unknown
        };
    }

    /// <summary>
    /// Reads one argument of a CSS transform function in the form that the function takes: a number for
    /// <c>matrix</c>, a number or percentage for the scale functions, an angle in degrees for the rotation and skew
    /// functions, and a <c>px</c> length or zero for the translation functions.
    /// </summary>
    /// <param name="source">The characters to read.</param>
    /// <param name="function">The function.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the argument.</param>
    /// <param name="length">When this method returns <see langword="true"/>, contains the number of characters read.</param>
    /// <returns><see langword="true"/> if an argument was read; otherwise, <see langword="false"/>.</returns>
    private static bool TryReadCssArgument(ReadOnlySpan<char> source, CssFunction function, out float value, out int length)
    {
        switch (function)
        {
            case CssFunction.Rotate:
            case CssFunction.Skew:
            case CssFunction.SkewX:
            case CssFunction.SkewY:
                return SvgNumberParser.TryReadAngle(source, false, out value, out length);
            case CssFunction.Translate:
            case CssFunction.TranslateX:
            case CssFunction.TranslateY:
                if (!SvgLength.TryRead(source, out SvgLength translation, out length)
                    || !(translation.Unit == SvgLengthUnit.Px || (translation.Unit == SvgLengthUnit.Number && translation.Value == 0F)))
                {
                    value = default;
                    length = 0;
                    return false;
                }

                value = translation.Value;
                return true;
            default:
                if (!SvgNumberParser.TryReadNumber(source, false, out value, out length))
                {
                    return false;
                }

                if (function is CssFunction.Scale or CssFunction.ScaleX or CssFunction.ScaleY
                    && length < source.Length
                    && source[length] == '%')
                {
                    value /= 100F;
                    length++;
                }

                return true;
        }
    }

    /// <summary>
    /// Creates the matrix for one CSS transform function.
    /// </summary>
    /// <param name="function">The function.</param>
    /// <param name="arguments">The arguments, with angles in degrees.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the matrix.</param>
    /// <returns><see langword="true"/> if the argument count is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreateCss(CssFunction function, ReadOnlySpan<float> arguments, out Matrix3x2 value)
    {
        int count = arguments.Length;
        value = Matrix3x2.Identity;
        switch (function)
        {
            case CssFunction.Matrix:
                return TryCreate("matrix", arguments, out value);
            case CssFunction.Translate when count is 1 or 2:
            case CssFunction.Scale when count is 1 or 2:
                return TryCreate(function == CssFunction.Translate ? "translate" : "scale", arguments, out value);
            case CssFunction.TranslateX when count == 1:
                value = Matrix3x2.CreateTranslation(arguments[0], 0F);
                return true;
            case CssFunction.TranslateY when count == 1:
                value = Matrix3x2.CreateTranslation(0F, arguments[0]);
                return true;
            case CssFunction.ScaleX when count == 1:
                value = Matrix3x2.CreateScale(arguments[0], 1F);
                return true;
            case CssFunction.ScaleY when count == 1:
                value = Matrix3x2.CreateScale(1F, arguments[0]);
                return true;
            case CssFunction.Rotate when count == 1:
                return TryCreate("rotate", arguments, out value);
            case CssFunction.Skew when count is 1 or 2:
                // skew(ax, ay) maps (x, y) to (x + tan(ax) * y, tan(ay) * x + y); ay defaults to 0.
                value.M21 = MathF.Tan(arguments[0] * (MathF.PI / 180F));
                value.M12 = count == 2 ? MathF.Tan(arguments[1] * (MathF.PI / 180F)) : 0F;
                return true;
            case CssFunction.SkewX when count == 1:
                return TryCreate("skewX", arguments, out value);
            case CssFunction.SkewY when count == 1:
                return TryCreate("skewY", arguments, out value);
            default:
                return false;
        }
    }

    /// <summary>
    /// Creates the matrix for one transform function.
    /// </summary>
    /// <param name="name">The function name.</param>
    /// <param name="arguments">The function arguments.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the matrix.</param>
    /// <returns><see langword="true"/> if the name is known and the argument count is valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryCreate(ReadOnlySpan<char> name, ReadOnlySpan<float> arguments, out Matrix3x2 value)
    {
        int count = arguments.Length;
        value = Matrix3x2.Identity;
        switch (name)
        {
            case "matrix" when count == 6:
                // matrix(a b c d e f) maps (x, y) to (a*x + c*y + e, b*x + d*y + f).
                value = new Matrix3x2(arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], arguments[5]);
                return true;
            case "translate" when count is 1 or 2:
                value = Matrix3x2.CreateTranslation(arguments[0], count == 2 ? arguments[1] : 0F);
                return true;
            case "scale" when count is 1 or 2:
                value = Matrix3x2.CreateScale(arguments[0], count == 2 ? arguments[1] : arguments[0]);
                return true;
            case "rotate" when count is 1 or 3:
                // rotate(a cx cy) is translate(cx cy) rotate(a) translate(-cx -cy), which CreateRotation builds with its
                // center point. A positive angle turns the x axis toward the y axis, which is clockwise on a y-down canvas.
                float radians = arguments[0] * (MathF.PI / 180F);
                value = count == 3
                    ? Matrix3x2.CreateRotation(radians, new Vector2(arguments[1], arguments[2]))
                    : Matrix3x2.CreateRotation(radians);

                return true;
            case "skewX" when count == 1:
                // skewX(a) maps (x, y) to (x + tan(a) * y, y).
                value.M21 = MathF.Tan(arguments[0] * (MathF.PI / 180F));
                return true;
            case "skewY" when count == 1:
                // skewY(a) maps (x, y) to (x, tan(a) * x + y).
                value.M12 = MathF.Tan(arguments[0] * (MathF.PI / 180F));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns whether every element of the matrix is finite.
    /// </summary>
    /// <param name="m">The matrix.</param>
    /// <returns><see langword="true"/> if the matrix is finite; otherwise, <see langword="false"/>.</returns>
    private static bool IsFinite(Matrix3x2 m)
        => float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M21)
        && float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32);
}
