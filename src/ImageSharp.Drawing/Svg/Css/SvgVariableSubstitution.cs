// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Replaces the <c>var()</c> functions of a value with the values of the custom properties they name, as CSS Custom
/// Properties Level 1, section 3 defines it.
/// </summary>
/// <remarks>
/// <para>
/// A reference to a custom property that is not declared, or that holds the guaranteed-invalid value, takes the fallback
/// of the <c>var()</c>, which may contain <c>var()</c> itself. Without a fallback the substitution fails, which makes the
/// declaration invalid at computed-value time. A <c>var()</c> whose first argument is not a custom property name is a
/// syntax error, which makes the declaration invalid at parse time instead.
/// </para>
/// <para>
/// CSS substitutes tokens, so a value never joins the text around it into one token: <c>rgb(0 var(--n)8 0)</c> with
/// <c>--n: 12</c> is <c>rgb(0 12 8 0)</c>. The text form here inserts a space where the characters on both sides of a
/// substitution would join.
/// </para>
/// <para>
/// Limits, which make a declaration invalid at computed-value time when exceeded: a substituted value longer than
/// <see cref="MaxValueLength"/> characters (a chain of custom properties that doubles its length at each step reaches it
/// after about 15 steps); <c>var()</c> nested in fallbacks, or custom properties that refer to each other, deeper than
/// <see cref="MaxDepth"/>; and more than <see cref="SvgSubstitutionBudget.MaxCharacters"/> characters produced by all
/// substitutions of one document.
/// </para>
/// </remarks>
internal static class SvgVariableSubstitution
{
    /// <summary>
    /// The longest substituted value, in characters.
    /// </summary>
    public const int MaxValueLength = 32 * 1024;

    /// <summary>
    /// The deepest nesting of <c>var()</c> in fallbacks and of custom properties that refer to each other.
    /// </summary>
    public const int MaxDepth = 32;

    /// <summary>
    /// The outcome of a substitution.
    /// </summary>
    public enum Outcome
    {
        /// <summary>
        /// The value has no <c>var()</c> left.
        /// </summary>
        Substituted,

        /// <summary>
        /// A reference has no value and no fallback, or a limit was exceeded: the declaration is invalid at computed-value
        /// time.
        /// </summary>
        Failed,

        /// <summary>
        /// A <c>var()</c> is malformed: the declaration is invalid at parse time.
        /// </summary>
        InvalidSyntax
    }

    /// <summary>
    /// The values that <c>var()</c> references resolve against.
    /// </summary>
    public interface ISource
    {
        /// <summary>
        /// Looks up a custom property.
        /// </summary>
        /// <param name="name">The name, with its leading <c>--</c>.</param>
        /// <param name="depth">The nesting depth of the reference, for <see cref="MaxDepth"/>.</param>
        /// <param name="value">
        /// When this method returns <see langword="true"/>, contains the value without <c>var()</c>, or
        /// <see langword="null"/> for the guaranteed-invalid value.
        /// </param>
        /// <returns><see langword="true"/> if the property is declared; otherwise, <see langword="false"/>.</returns>
        bool TryGetValue(ReadOnlySpan<char> name, int depth, out string? value);
    }

    /// <summary>
    /// Substitutes every <c>var()</c> of a value.
    /// </summary>
    /// <param name="text">The value.</param>
    /// <param name="source">The custom properties.</param>
    /// <param name="budget">The work budget of the document.</param>
    /// <param name="depth">The nesting depth of the value: 0 for a declaration.</param>
    /// <param name="result">When this method returns <see cref="Outcome.Substituted"/>, contains the value.</param>
    /// <returns>The outcome.</returns>
    public static Outcome Substitute(ReadOnlySpan<char> text, ISource source, SvgSubstitutionBudget budget, int depth, out string? result)
    {
        result = null;
        StringBuilder builder = new();
        Outcome outcome = Append(text, source, budget, depth, builder);
        if (outcome == Outcome.Substituted)
        {
            result = builder.ToString();
        }

        return outcome;
    }

    /// <summary>
    /// Substitutes the <c>var()</c> functions of a value and appends the result.
    /// </summary>
    /// <param name="text">The value.</param>
    /// <param name="source">The custom properties.</param>
    /// <param name="budget">The work budget of the document.</param>
    /// <param name="depth">The nesting depth of the value.</param>
    /// <param name="builder">The builder that receives the result.</param>
    /// <returns>The outcome.</returns>
    private static Outcome Append(ReadOnlySpan<char> text, ISource source, SvgSubstitutionBudget budget, int depth, StringBuilder builder)
    {
        if (depth > MaxDepth)
        {
            return Outcome.Failed;
        }

        int position = 0;
        while (position < text.Length)
        {
            int start = FindVar(text, position);
            if (start < 0)
            {
                return AppendText(text[position..], false, budget, builder) ? Outcome.Substituted : Outcome.Failed;
            }

            if (!AppendText(text[position..start], false, budget, builder))
            {
                return Outcome.Failed;
            }

            // var( <custom-property-name> [, <declaration-value>?]? )
            int cursor = SkipWhitespace(text, start + 4);
            int nameStart = cursor;
            while (cursor < text.Length && IsNameCharacter(text[cursor]))
            {
                cursor++;
            }

            ReadOnlySpan<char> name = text[nameStart..cursor];
            if (name.Length < 3 || name[0] != '-' || name[1] != '-')
            {
                return Outcome.InvalidSyntax;
            }

            cursor = SkipWhitespace(text, cursor);
            if (cursor >= text.Length)
            {
                return Outcome.InvalidSyntax;
            }

            ReadOnlySpan<char> fallback = default;
            bool hasFallback = false;
            if (text[cursor] == ',')
            {
                int end = FindClose(text, cursor + 1);
                if (end < 0)
                {
                    return Outcome.InvalidSyntax;
                }

                fallback = text[(cursor + 1)..end];
                hasFallback = true;
                cursor = end;
            }
            else if (text[cursor] != ')')
            {
                return Outcome.InvalidSyntax;
            }

            position = cursor + 1;

            // A substituted value is separated from the text before it when the two would join into one token.
            int mark = builder.Length;
            if (source.TryGetValue(name, depth + 1, out string? value) && value is not null)
            {
                if (!AppendText(value, true, budget, builder))
                {
                    return Outcome.Failed;
                }
            }
            else if (hasFallback)
            {
                // The fallback is substituted only when it is used, and its references nest one level deeper.
                Outcome outcome = Append(SvgNumberParser.Trim(fallback), source, budget, depth + 1, builder);
                if (outcome != Outcome.Substituted)
                {
                    return outcome;
                }

                SeparateStart(builder, mark);
            }
            else
            {
                return Outcome.Failed;
            }

            // The text after the substitution must not join the end of the value either.
            if (position < text.Length && builder.Length > 0 && Joins(builder[^1]) && Joins(text[position]))
            {
                _ = builder.Append(' ');
            }
        }

        return Outcome.Substituted;
    }

    /// <summary>
    /// Appends text and charges the budget.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="separate">Whether the text is a substituted value, which must not join the text before it.</param>
    /// <param name="budget">The work budget of the document.</param>
    /// <param name="builder">The builder.</param>
    /// <returns><see langword="true"/> while the value and the document are within their limits.</returns>
    private static bool AppendText(ReadOnlySpan<char> text, bool separate, SvgSubstitutionBudget budget, StringBuilder builder)
    {
        if (text.IsEmpty)
        {
            return true;
        }

        if (separate && builder.Length > 0 && Joins(builder[^1]) && Joins(text[0]))
        {
            _ = builder.Append(' ');
        }

        if (builder.Length + text.Length > MaxValueLength || !budget.TryCharge(text.Length))
        {
            return false;
        }

        _ = builder.Append(text);
        return true;
    }

    /// <summary>
    /// Inserts a space at the start of a substituted fallback when it would join the text before it.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="mark">The length of the builder before the fallback.</param>
    private static void SeparateStart(StringBuilder builder, int mark)
    {
        if (mark > 0 && builder.Length > mark && Joins(builder[mark - 1]) && Joins(builder[mark]))
        {
            _ = builder.Insert(mark, ' ');
        }
    }

    /// <summary>
    /// Returns whether a character continues a number, a name, or a hash when another such character follows it.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> if the character takes part in joining; otherwise, <see langword="false"/>.</returns>
    private static bool Joins(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '%' or '#' || c > 0x7F;

    /// <summary>
    /// Returns whether a character can be part of a custom property name.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for a name character; otherwise, <see langword="false"/>.</returns>
    private static bool IsNameCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || c > 0x7F;

    /// <summary>
    /// Skips white space.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The position to start at.</param>
    /// <returns>The position of the first character that is not white space.</returns>
    private static int SkipWhitespace(ReadOnlySpan<char> text, int position)
    {
        while (position < text.Length && SvgNumberParser.IsWhitespace(text[position]))
        {
            position++;
        }

        return position;
    }

    /// <summary>
    /// Finds the next <c>var(</c> function token outside a string.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The position to search from.</param>
    /// <returns>The position of the <c>v</c>, or -1.</returns>
    private static int FindVar(ReadOnlySpan<char> text, int position)
    {
        char quote = '\0';
        for (int i = position; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            // The function name matches ignoring ASCII case, and must not be the end of a longer name.
            if (c is 'v' or 'V'
                && i + 3 < text.Length
                && text[i + 3] == '('
                && text.Slice(i, 3).Equals("var", StringComparison.OrdinalIgnoreCase)
                && (i == 0 || !IsNameCharacter(text[i - 1])))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the parenthesis that closes a function whose arguments start at a position, outside strings and nested
    /// parentheses.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="position">The position after the opening parenthesis or after the comma of a fallback.</param>
    /// <returns>The position of the closing parenthesis, or -1.</returns>
    private static int FindClose(ReadOnlySpan<char> text, int position)
    {
        int nesting = 0;
        char quote = '\0';
        for (int i = position; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            switch (c)
            {
                case '"':
                case '\'':
                    quote = c;
                    break;
                case '(':
                    nesting++;
                    break;
                case ')' when nesting == 0:
                    return i;
                case ')':
                    nesting--;
                    break;
            }
        }

        return -1;
    }
}
