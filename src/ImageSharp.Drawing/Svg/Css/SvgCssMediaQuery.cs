// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Evaluates media query lists (Media Queries Level 4) for the <c>media</c> attribute of <c>style</c> elements and for
/// <c>@media</c> rules.
/// </summary>
/// <remarks>
/// <para>
/// The document renders on a screen: the media types <c>all</c> and <c>screen</c> match, every other type does not. The
/// features <c>width</c>, <c>height</c> (with the <c>min-</c> and <c>max-</c> prefixes and the range syntax),
/// <c>aspect-ratio</c>, and <c>orientation</c> are evaluated against the viewport that the caller gives. Every other
/// feature is unknown. Conditions use the three-valued logic of the specification: an unknown feature makes its condition
/// unknown, <c>not</c> keeps it unknown, and an unknown query does not match. A malformed query is <c>not all</c> and
/// does not match; the other queries of its list still count.
/// </para>
/// <para>
/// Relative lengths use the initial font size of 16 pixels, as media queries require.
/// </para>
/// </remarks>
internal static class SvgCssMediaQuery
{
    /// <summary>
    /// The maximum nesting depth of parentheses in a media condition. Deeper conditions are malformed.
    /// </summary>
    private const int MaxNesting = 16;

    /// <summary>
    /// The three-valued result of a media condition.
    /// </summary>
    private enum Result : byte
    {
        /// <summary>
        /// The condition is false.
        /// </summary>
        False,

        /// <summary>
        /// The condition is true.
        /// </summary>
        True,

        /// <summary>
        /// The condition uses an unknown feature or syntax.
        /// </summary>
        Unknown
    }

    /// <summary>
    /// Evaluates a media query list.
    /// </summary>
    /// <param name="text">The media query list text.</param>
    /// <param name="viewport">The viewport size in CSS pixels.</param>
    /// <returns><see langword="true"/> if the list matches; otherwise, <see langword="false"/>.</returns>
    public static bool Evaluate(ReadOnlySpan<char> text, SizeF viewport)
    {
        ValueListBuilder<SvgCssToken> builder = new(stackalloc SvgCssToken[32]);
        try
        {
            // Whitespace and comments carry no meaning in a media query, so only the other tokens are kept.
            SvgCssTokenizer tokenizer = new(text);
            for (SvgCssToken token = tokenizer.Next(); token.Kind != SvgCssTokenKind.EndOfFile; token = tokenizer.Next())
            {
                if (token.Kind != SvgCssTokenKind.Whitespace)
                {
                    builder.Append(token);
                }
            }

            ReadOnlySpan<SvgCssToken> tokens = builder.AsSpan();

            // An empty list matches every medium.
            if (tokens.IsEmpty)
            {
                return true;
            }

            int start = 0;
            int depth = 0;
            for (int i = 0; i <= tokens.Length; i++)
            {
                if (i < tokens.Length)
                {
                    SvgCssTokenKind kind = tokens[i].Kind;
                    if (kind is SvgCssTokenKind.OpenParenthesis or SvgCssTokenKind.Function or SvgCssTokenKind.OpenSquare or SvgCssTokenKind.OpenCurly)
                    {
                        depth++;
                    }
                    else if (kind is SvgCssTokenKind.CloseParenthesis or SvgCssTokenKind.CloseSquare or SvgCssTokenKind.CloseCurly)
                    {
                        depth--;
                    }

                    if (kind != SvgCssTokenKind.Comma || depth != 0)
                    {
                        continue;
                    }
                }

                if (EvaluateQuery(text, tokens[start..i], viewport))
                {
                    return true;
                }

                start = i + 1;
            }

            return false;
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Evaluates one media query: <c>[not | only]? &lt;type&gt; [and &lt;condition&gt;]?</c> or
    /// <c>&lt;condition&gt;</c>.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens of the query, without whitespace.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <returns><see langword="true"/> if the query matches; otherwise, <see langword="false"/>.</returns>
    private static bool EvaluateQuery(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens, SizeF viewport)
    {
        if (tokens.IsEmpty)
        {
            return false;
        }

        if (tokens[0].Kind != SvgCssTokenKind.Ident || (IsKeyword(text, tokens[0], "not") && tokens.Length > 1 && tokens[1].Kind != SvgCssTokenKind.Ident))
        {
            // A condition without a media type, such as "(min-width: 10px)" or "not (orientation: portrait)".
            int end = 0;
            Result condition = EvaluateCondition(text, tokens, ref end, viewport, 0);
            return end == tokens.Length && condition == Result.True;
        }

        int i = 0;
        bool negate = false;
        if (IsKeyword(text, tokens[0], "not") || IsKeyword(text, tokens[0], "only"))
        {
            negate = IsKeyword(text, tokens[0], "not");
            i++;
        }

        if (i >= tokens.Length || tokens[i].Kind != SvgCssTokenKind.Ident)
        {
            return false;
        }

        // The keywords of the grammar cannot be media types.
        SvgCssToken type = tokens[i++];
        if (IsKeyword(text, type, "not") || IsKeyword(text, type, "and") || IsKeyword(text, type, "or") || IsKeyword(text, type, "only"))
        {
            return false;
        }

        Result result = IsKeyword(text, type, "all") || IsKeyword(text, type, "screen") ? Result.True : Result.False;
        if (i < tokens.Length)
        {
            // "<type> and <condition-without-or>".
            if (!IsKeyword(text, tokens[i], "and") || i + 1 >= tokens.Length)
            {
                return false;
            }

            i++;
            Result condition = EvaluateCondition(text, tokens, ref i, viewport, 0);
            if (i != tokens.Length || ContainsOr(text, tokens))
            {
                return false;
            }

            result = And(result, condition);
        }

        if (result == Result.Unknown)
        {
            return false;
        }

        return negate ? result == Result.False : result == Result.True;
    }

    /// <summary>
    /// Returns whether a token range has a top-level <c>or</c> keyword, which is not allowed after a media type.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens.</param>
    /// <returns><see langword="true"/> if the range has one; otherwise, <see langword="false"/>.</returns>
    private static bool ContainsOr(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens)
    {
        int depth = 0;
        foreach (SvgCssToken token in tokens)
        {
            if (token.Kind is SvgCssTokenKind.OpenParenthesis or SvgCssTokenKind.Function)
            {
                depth++;
            }
            else if (token.Kind == SvgCssTokenKind.CloseParenthesis)
            {
                depth--;
            }
            else if (depth == 0 && IsKeyword(text, token, "or"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates a media condition: <c>not &lt;in-parens&gt;</c>, or <c>&lt;in-parens&gt;</c> followed by any number of
    /// <c>and &lt;in-parens&gt;</c>, or by any number of <c>or &lt;in-parens&gt;</c>.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens.</param>
    /// <param name="i">The first token; advanced past the condition.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <param name="depth">The parenthesis nesting depth.</param>
    /// <returns>The result; <see cref="Result.Unknown"/> for malformed input, with <paramref name="i"/> left short.</returns>
    private static Result EvaluateCondition(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens, ref int i, SizeF viewport, int depth)
    {
        if (i < tokens.Length && IsKeyword(text, tokens[i], "not"))
        {
            i++;
            Result operand = EvaluateInParens(text, tokens, ref i, viewport, depth);
            return operand switch
            {
                Result.True => Result.False,
                Result.False => Result.True,
                _ => Result.Unknown
            };
        }

        Result result = EvaluateInParens(text, tokens, ref i, viewport, depth);
        if (i < 0)
        {
            return Result.Unknown;
        }

        bool? isAnd = null;
        while (i < tokens.Length && tokens[i].Kind == SvgCssTokenKind.Ident)
        {
            bool and = IsKeyword(text, tokens[i], "and");
            if ((!and && !IsKeyword(text, tokens[i], "or")) || (isAnd is bool previous && previous != and))
            {
                // Another word, or "and" mixed with "or" without parentheses.
                i = -1;
                return Result.Unknown;
            }

            isAnd = and;
            i++;
            Result operand = EvaluateInParens(text, tokens, ref i, viewport, depth);
            if (i < 0)
            {
                return Result.Unknown;
            }

            result = and ? And(result, operand) : Or(result, operand);
        }

        return result;
    }

    /// <summary>
    /// Evaluates <c>( &lt;condition&gt; )</c> or <c>( &lt;feature&gt; )</c>. Anything else in parentheses, or a function,
    /// is general enclosed content and unknown.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens.</param>
    /// <param name="i">The first token; advanced past the closing parenthesis, or set to -1 for malformed input.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <param name="depth">The parenthesis nesting depth.</param>
    /// <returns>The result.</returns>
    private static Result EvaluateInParens(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens, ref int i, SizeF viewport, int depth)
    {
        if (i < 0 || i >= tokens.Length || depth >= MaxNesting
            || tokens[i].Kind is not (SvgCssTokenKind.OpenParenthesis or SvgCssTokenKind.Function))
        {
            i = -1;
            return Result.Unknown;
        }

        // Find the matching closing parenthesis.
        bool function = tokens[i].Kind == SvgCssTokenKind.Function;
        int open = i;
        int level = 0;
        int close = -1;
        for (int t = open; t < tokens.Length; t++)
        {
            SvgCssTokenKind kind = tokens[t].Kind;
            if (kind is SvgCssTokenKind.OpenParenthesis or SvgCssTokenKind.Function)
            {
                level++;
            }
            else if (kind == SvgCssTokenKind.CloseParenthesis && --level == 0)
            {
                close = t;
                break;
            }
        }

        if (close < 0)
        {
            i = -1;
            return Result.Unknown;
        }

        i = close + 1;
        ReadOnlySpan<SvgCssToken> inner = tokens[(open + 1)..close];
        if (function || inner.IsEmpty)
        {
            return Result.Unknown;
        }

        if (inner[0].Kind == SvgCssTokenKind.OpenParenthesis || IsKeyword(text, inner[0], "not"))
        {
            int end = 0;
            Result nested = EvaluateCondition(text, inner, ref end, viewport, depth + 1);
            return end == inner.Length ? nested : Result.Unknown;
        }

        return EvaluateFeature(text, inner, viewport);
    }

    /// <summary>
    /// Evaluates a media feature: <c>name</c>, <c>name: value</c>, or a range such as <c>width &gt;= 300px</c> or
    /// <c>100px &lt; width &lt; 300px</c>.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens inside the parentheses.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <returns>The result.</returns>
    private static Result EvaluateFeature(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens, SizeF viewport)
    {
        if (tokens[0].Kind == SvgCssTokenKind.Ident && (tokens.Length == 1 || tokens[1].Kind == SvgCssTokenKind.Colon))
        {
            ReadOnlySpan<char> name = Name(text, tokens[0]);
            if (tokens.Length == 1)
            {
                // Boolean context: true when the feature value is not zero or none.
                if (!TryGetFeature(name, viewport, out float boolValue, out _))
                {
                    return Result.Unknown;
                }

                return boolValue != 0F ? Result.True : Result.False;
            }

            int prefix = 0;
            if (name.Length > 4 && Ascii.EqualsIgnoreCase(name[..4], "min-"))
            {
                prefix = 1;
                name = name[4..];
            }
            else if (name.Length > 4 && Ascii.EqualsIgnoreCase(name[..4], "max-"))
            {
                prefix = -1;
                name = name[4..];
            }

            if (Ascii.EqualsIgnoreCase(name, "orientation"))
            {
                if (prefix != 0 || tokens.Length != 3 || tokens[2].Kind != SvgCssTokenKind.Ident)
                {
                    return Result.Unknown;
                }

                bool portrait = viewport.Height >= viewport.Width;
                if (IsKeyword(text, tokens[2], "portrait"))
                {
                    return portrait ? Result.True : Result.False;
                }

                return IsKeyword(text, tokens[2], "landscape") ? (portrait ? Result.False : Result.True) : Result.Unknown;
            }

            if (!TryGetFeature(name, viewport, out float actual, out bool isRatio))
            {
                return Result.Unknown;
            }

            int index = 2;
            if (!TryParseValue(text, tokens, ref index, isRatio, out float expected) || index != tokens.Length)
            {
                return Result.Unknown;
            }

            bool holds = prefix > 0 ? actual >= expected : prefix < 0 ? actual <= expected : actual == expected;
            return holds ? Result.True : Result.False;
        }

        return EvaluateRange(text, tokens, viewport);
    }

    /// <summary>
    /// Evaluates the range syntax of a media feature.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens inside the parentheses.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <returns>The result.</returns>
    private static Result EvaluateRange(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens, SizeF viewport)
    {
        // Find the feature name: the only identifier outside a value. Lengths and ratios are not identifiers.
        int nameIndex = -1;
        for (int t = 0; t < tokens.Length; t++)
        {
            if (tokens[t].Kind == SvgCssTokenKind.Ident)
            {
                if (nameIndex >= 0)
                {
                    return Result.Unknown;
                }

                nameIndex = t;
            }
        }

        if (nameIndex < 0 || !TryGetFeature(Name(text, tokens[nameIndex]), viewport, out float actual, out bool isRatio))
        {
            return Result.Unknown;
        }

        // "value op name [op value]" or "name op value".
        Result result = Result.True;
        int i = 0;
        if (nameIndex > 0)
        {
            if (!TryParseValue(text, tokens, ref i, isRatio, out float left)
                || !TryParseComparison(text, tokens, ref i, out int comparison, out bool orEqual)
                || i != nameIndex)
            {
                return Result.Unknown;
            }

            result = Compare(left, actual, comparison, orEqual) ? Result.True : Result.False;
            i = nameIndex + 1;
            if (i == tokens.Length)
            {
                return result;
            }

            // Both comparisons of "a < name < b" must point the same way, and neither may be "=".
            if (!TryParseComparison(text, tokens, ref i, out int second, out bool secondOrEqual)
                || comparison == 0 || second != comparison
                || !TryParseValue(text, tokens, ref i, isRatio, out float right) || i != tokens.Length)
            {
                return Result.Unknown;
            }

            return result == Result.True && Compare(actual, right, second, secondOrEqual) ? Result.True : Result.False;
        }

        i = 1;
        if (!TryParseComparison(text, tokens, ref i, out int op, out bool opOrEqual)
            || !TryParseValue(text, tokens, ref i, isRatio, out float value) || i != tokens.Length)
        {
            return Result.Unknown;
        }

        return Compare(actual, value, op, opOrEqual) ? Result.True : Result.False;
    }

    /// <summary>
    /// Compares two values with a range operator.
    /// </summary>
    /// <param name="left">The left value.</param>
    /// <param name="right">The right value.</param>
    /// <param name="comparison">-1 for <c>&lt;</c>, 1 for <c>&gt;</c>, 0 for <c>=</c>.</param>
    /// <param name="orEqual">Whether the operator includes equality.</param>
    /// <returns><see langword="true"/> if the comparison holds; otherwise, <see langword="false"/>.</returns>
    private static bool Compare(float left, float right, int comparison, bool orEqual)
        => comparison switch
        {
            < 0 => left < right || (orEqual && left == right),
            > 0 => left > right || (orEqual && left == right),
            _ => left == right
        };

    /// <summary>
    /// Parses a range operator: <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c>, <c>&gt;=</c>, or <c>=</c>.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens.</param>
    /// <param name="i">The token index; advanced past the operator.</param>
    /// <param name="comparison">-1 for less, 1 for greater, 0 for equal.</param>
    /// <param name="orEqual">Whether the operator includes equality.</param>
    /// <returns><see langword="true"/> if an operator was read; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseComparison(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens, ref int i, out int comparison, out bool orEqual)
    {
        comparison = 0;
        orEqual = false;
        if (i >= tokens.Length || tokens[i].Kind != SvgCssTokenKind.Delim)
        {
            return false;
        }

        char c = text[tokens[i].Start];
        if (c == '=')
        {
            i++;
            return true;
        }

        if (c is not ('<' or '>'))
        {
            return false;
        }

        comparison = c == '<' ? -1 : 1;
        i++;

        // "<=" is two tokens with nothing between them.
        if (i < tokens.Length && tokens[i].IsDelimiter(text, '=') && tokens[i].Start == tokens[i - 1].End)
        {
            orEqual = true;
            i++;
        }

        return true;
    }

    /// <summary>
    /// Gets the value of a media feature for the viewport.
    /// </summary>
    /// <param name="name">The feature name without prefix.</param>
    /// <param name="viewport">The viewport size.</param>
    /// <param name="value">The value: a length in pixels, or a ratio.</param>
    /// <param name="isRatio">Whether the feature takes a ratio.</param>
    /// <returns><see langword="true"/> for a known feature; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetFeature(ReadOnlySpan<char> name, SizeF viewport, out float value, out bool isRatio)
    {
        isRatio = false;
        if (Ascii.EqualsIgnoreCase(name, "width"))
        {
            value = viewport.Width;
            return true;
        }

        if (Ascii.EqualsIgnoreCase(name, "height"))
        {
            value = viewport.Height;
            return true;
        }

        if (Ascii.EqualsIgnoreCase(name, "aspect-ratio"))
        {
            isRatio = true;
            value = viewport.Height > 0F ? viewport.Width / viewport.Height : 0F;
            return true;
        }

        value = 0F;
        return false;
    }

    /// <summary>
    /// Parses a feature value: a length, or a ratio such as <c>16/9</c> for a ratio feature.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="tokens">The tokens.</param>
    /// <param name="i">The token index; advanced past the value.</param>
    /// <param name="isRatio">Whether a ratio is expected.</param>
    /// <param name="value">The value in pixels, or the ratio.</param>
    /// <returns><see langword="true"/> if a value was read; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseValue(ReadOnlySpan<char> text, ReadOnlySpan<SvgCssToken> tokens, ref int i, bool isRatio, out float value)
    {
        value = 0F;
        if (i >= tokens.Length)
        {
            return false;
        }

        SvgCssToken token = tokens[i];
        if (isRatio)
        {
            if (token.Kind != SvgCssTokenKind.Number || !SvgNumberParser.TryParseNumber(token.GetRawValue(text), out float numerator) || numerator < 0F)
            {
                return false;
            }

            i++;
            value = numerator;
            if (i + 1 < tokens.Length && tokens[i].IsDelimiter(text, '/') && tokens[i + 1].Kind == SvgCssTokenKind.Number)
            {
                if (!SvgNumberParser.TryParseNumber(tokens[i + 1].GetRawValue(text), out float denominator) || denominator < 0F)
                {
                    return false;
                }

                value = denominator == 0F ? float.PositiveInfinity : numerator / denominator;
                i += 2;
            }

            return true;
        }

        // A length; a bare number is allowed only for zero.
        if (token.Kind == SvgCssTokenKind.Number)
        {
            if (!SvgNumberParser.TryParseNumber(token.GetRawValue(text), out float number) || number != 0F)
            {
                return false;
            }

            i++;
            return true;
        }

        if (token.Kind != SvgCssTokenKind.Dimension || token.HasEscape
            || !SvgLength.TryParse(text[token.Start..token.End], out SvgLength length)
            || length.Unit is SvgLengthUnit.Number or SvgLengthUnit.Percent)
        {
            return false;
        }

        // Relative units use the initial font size; viewport units use the viewport itself.
        value = SvgUnits.ToPixels(length, SvgUnitBasis.InitialFontSize, new SvgUnitBasis(96F, SvgUnitBasis.InitialFontSize, new SizeF(0F, 0F)));
        i++;
        return true;
    }

    /// <summary>
    /// Returns the value of an identifier token with escapes decoded.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="token">The token.</param>
    /// <returns>The name.</returns>
    private static ReadOnlySpan<char> Name(ReadOnlySpan<char> text, SvgCssToken token)
        => token.HasEscape ? token.GetValue(text) : token.GetRawValue(text);

    /// <summary>
    /// Returns whether a token is an identifier equal to an ASCII keyword, ignoring ASCII case.
    /// </summary>
    /// <param name="text">The source text.</param>
    /// <param name="token">The token.</param>
    /// <param name="keyword">The lowercase keyword.</param>
    /// <returns><see langword="true"/> if the token is the keyword; otherwise, <see langword="false"/>.</returns>
    private static bool IsKeyword(ReadOnlySpan<char> text, SvgCssToken token, string keyword)
        => token.Kind == SvgCssTokenKind.Ident && Ascii.EqualsIgnoreCase(Name(text, token), keyword);

    /// <summary>
    /// Combines two results with the three-valued <c>and</c>.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns>The combined result.</returns>
    private static Result And(Result left, Result right)
        => left == Result.False || right == Result.False ? Result.False
        : left == Result.Unknown || right == Result.Unknown ? Result.Unknown
        : Result.True;

    /// <summary>
    /// Combines two results with the three-valued <c>or</c>.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns>The combined result.</returns>
    private static Result Or(Result left, Result right)
        => left == Result.True || right == Result.True ? Result.True
        : left == Result.Unknown || right == Result.Unknown ? Result.Unknown
        : Result.False;
}
