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
