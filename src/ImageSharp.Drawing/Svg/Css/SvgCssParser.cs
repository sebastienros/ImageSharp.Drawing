// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Text;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses style sheets and declaration lists with the parser algorithms of CSS Syntax Level 3, section 5.
/// </summary>
/// <remarks>
/// <para>
/// The parser streams over <see cref="SvgCssTokenizer"/> tokens and builds no component value tree. Blocks that it does
/// not use (at-rules other than <c>@media</c>, and nested blocks inside preludes and values) are skipped with a stack of
/// the expected closing tokens, so input such as unbalanced brackets or deep nesting cannot overflow the call stack and
/// the parser stays in sync. The recovery rules follow the CSS Syntax parser of the Jint project (BSD 2-Clause License,
/// see THIRD-PARTY-NOTICES).
/// </para>
/// <para>
/// Selectors are parsed by <see cref="SvgCssSelectorParser"/>. A selector list with an invalid selector drops the whole
/// rule, as CSS requires. The rules of an <c>@media</c> block are kept with the media condition of the block, which
/// <see cref="SvgStyleSheet.Complete(SizeF)"/> evaluates once the viewport is known. <c>@media</c> blocks nest up to
/// <see cref="SvgStyleSheet.MaxMediaNesting"/> levels; deeper blocks are skipped.
/// </para>
/// <para>
/// Escapes are decoded in property names, selectors, and declaration values. A value with an escape is rebuilt from its
/// decoded tokens before the value parsers, which do not know CSS escapes, see it.
/// </para>
/// </remarks>
internal static class SvgCssParser
{
    /// <summary>
    /// The characters that a decoded URL cannot contain without escapes.
    /// </summary>
    private static readonly SearchValues<char> UrlTerminators = SearchValues.Create("()\"'\\ \t\n\r\f");

    /// <summary>
    /// Parses an author style sheet and appends its rules to <paramref name="sheet"/>.
    /// </summary>
    /// <param name="text">The style sheet text.</param>
    /// <param name="sheet">The style sheet that receives the rules.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="names">The pool that interns class names.</param>
    public static void ParseStyleSheet(ReadOnlySpan<char> text, SvgStyleSheet sheet, SvgPropertyParser properties, SvgStringPool names)
        => ParseStyleSheet(text, sheet, properties, names, false, -1);

    /// <summary>
    /// Parses a style sheet and appends its rules to <paramref name="sheet"/>.
    /// </summary>
    /// <param name="text">The style sheet text.</param>
    /// <param name="sheet">The style sheet that receives the rules.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="names">The pool that interns class names.</param>
    /// <param name="isUser">Whether the rules have the user origin.</param>
    /// <param name="media">The media condition of the whole style sheet, or -1.</param>
    public static void ParseStyleSheet(ReadOnlySpan<char> text, SvgStyleSheet sheet, SvgPropertyParser properties, SvgStringPool names, bool isUser, int media)
        => ParseRules(text, sheet, properties, names, isUser, media, 0);

    /// <summary>
    /// Parses a declaration list, as in a <c>style</c> attribute or a rule block, into two styles: one for normal
    /// declarations and one for <c>!important</c> declarations. Later declarations override earlier ones of the same
    /// importance. Unknown properties and invalid values are dropped.
    /// </summary>
    /// <param name="text">The declaration list text.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="normal">The style that receives the normal declarations.</param>
    /// <param name="important">The style that receives the important declarations.</param>
    public static void ParseDeclarations(ReadOnlySpan<char> text, SvgPropertyParser properties, ref SvgSpecifiedStyle normal, ref SvgSpecifiedStyle important)
    {
        // CSS Syntax 5.4.5, consume a list of declarations.
        SvgCssTokenizer tokenizer = new(text);
        while (true)
        {
            SvgCssToken token = tokenizer.Next();
            switch (token.Kind)
            {
                case SvgCssTokenKind.EndOfFile:
                    return;
                case SvgCssTokenKind.Whitespace:
                case SvgCssTokenKind.Semicolon:
                    continue;
                case SvgCssTokenKind.AtKeyword:
                    SkipAtRule(ref tokenizer);
                    continue;
            }

            // A declaration starts with an identifier. Anything else is a parse error that skips to the next ";".
            bool isDeclaration = token.Kind == SvgCssTokenKind.Ident;
            int start = token.Start;
            int end = SkipToSemicolon(ref tokenizer, token, text.Length);
            if (isDeclaration)
            {
                ParseDeclaration(text[start..end], properties, ref normal, ref important);
            }
        }
    }

    /// <summary>
    /// Parses a list of rules: the top level of a style sheet or the block of an <c>@media</c> rule.
    /// </summary>
    /// <param name="text">The rule list text.</param>
    /// <param name="sheet">The style sheet that receives the rules.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="names">The pool that interns class names.</param>
    /// <param name="isUser">Whether the rules have the user origin.</param>
    /// <param name="media">The media condition of the rules, or -1.</param>
    /// <param name="depth">The <c>@media</c> nesting depth.</param>
    private static void ParseRules(
        ReadOnlySpan<char> text,
        SvgStyleSheet sheet,
        SvgPropertyParser properties,
        SvgStringPool names,
        bool isUser,
        int media,
        int depth)
    {
        // CSS Syntax 5.4.1, consume a list of rules. CDO and CDC are skipped in nested lists too, as browsers do.
        SvgCssTokenizer tokenizer = new(text);
        while (true)
        {
            SvgCssToken token = tokenizer.Next();
            switch (token.Kind)
            {
                case SvgCssTokenKind.EndOfFile:
                    return;
                case SvgCssTokenKind.Whitespace:
                case SvgCssTokenKind.Cdo:
                case SvgCssTokenKind.Cdc:
                    continue;
                case SvgCssTokenKind.AtKeyword:
                    if (depth < SvgStyleSheet.MaxMediaNesting
                        && Ascii.EqualsIgnoreCase(token.HasEscape ? token.GetValue(text) : token.GetRawValue(text), "media"))
                    {
                        ParseMediaRule(ref tokenizer, text, sheet, properties, names, isUser, media, depth);
                    }
                    else
                    {
                        // Other at-rules, such as @import, @font-face, and @supports, are not supported and are skipped
                        // whole.
                        SkipAtRule(ref tokenizer);
                    }

                    continue;
            }

            // CSS Syntax 5.4.3, consume a qualified rule: the prelude runs to the "{" of the block.
            int preludeStart = token.Start;
            while (token.Kind != SvgCssTokenKind.OpenCurly)
            {
                if (token.Kind == SvgCssTokenKind.EndOfFile)
                {
                    // A rule without a block is a parse error and is dropped.
                    return;
                }

                SkipComponentValue(ref tokenizer, token);
                token = tokenizer.Next();
            }

            int preludeEnd = token.Start;
            int blockStart = token.End;
            int blockEnd = SkipBlock(ref tokenizer, SvgCssTokenKind.CloseCurly, text.Length);
            AddRule(text[preludeStart..preludeEnd], text[blockStart..blockEnd], sheet, properties, names, isUser, media);
        }
    }

    /// <summary>
    /// Parses an <c>@media</c> rule after its keyword: the media query list, then the rules of its block.
    /// </summary>
    /// <param name="tokenizer">The tokenizer, positioned after the at-keyword.</param>
    /// <param name="text">The rule list text.</param>
    /// <param name="sheet">The style sheet that receives the rules.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="names">The pool that interns class names.</param>
    /// <param name="isUser">Whether the rules have the user origin.</param>
    /// <param name="media">The media condition of the enclosing rules, or -1.</param>
    /// <param name="depth">The <c>@media</c> nesting depth of the enclosing rules.</param>
    private static void ParseMediaRule(
        ref SvgCssTokenizer tokenizer,
        ReadOnlySpan<char> text,
        SvgStyleSheet sheet,
        SvgPropertyParser properties,
        SvgStringPool names,
        bool isUser,
        int media,
        int depth)
    {
        int preludeStart = tokenizer.Position;
        while (true)
        {
            SvgCssToken token = tokenizer.Next();
            switch (token.Kind)
            {
                case SvgCssTokenKind.EndOfFile:
                case SvgCssTokenKind.Semicolon:
                    // An @media rule without a block has no rules.
                    return;
                case SvgCssTokenKind.OpenCurly:
                    int blockStart = token.End;
                    int blockEnd = SkipBlock(ref tokenizer, SvgCssTokenKind.CloseCurly, text.Length);
                    int condition = sheet.AddMedia(text[preludeStart..token.Start].ToString(), media);
                    ParseRules(text[blockStart..blockEnd], sheet, properties, names, isUser, condition, depth + 1);
                    return;
                default:
                    SkipComponentValue(ref tokenizer, token);
                    break;
            }
        }
    }

    /// <summary>
    /// Parses the selector list of a rule and, when it is valid, adds one rule per selector.
    /// </summary>
    /// <param name="prelude">The selector list text.</param>
    /// <param name="block">The declaration block text, without braces.</param>
    /// <param name="sheet">The style sheet that receives the rules.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="names">The pool that interns class names.</param>
    /// <param name="isUser">Whether the rule has the user origin.</param>
    /// <param name="media">The media condition of the rule, or -1.</param>
    private static void AddRule(
        ReadOnlySpan<char> prelude,
        ReadOnlySpan<char> block,
        SvgStyleSheet sheet,
        SvgPropertyParser properties,
        SvgStringPool names,
        bool isUser,
        int media)
    {
        int simpleStart = sheet.SimpleSelectors.Count;
        int compoundStart = sheet.Compounds.Count;
        int argumentStart = sheet.Arguments.Count;
        Span<int> selectorStorage = stackalloc int[16];
        ValueListBuilder<int> selectors = new(selectorStorage);
        try
        {
            // Each selector is stored as (first compound, compound count, specificity) in the builder.
            if (!SvgCssSelectorParser.TryParse(prelude, sheet, names, ref selectors))
            {
                sheet.SimpleSelectors.RemoveRange(simpleStart, sheet.SimpleSelectors.Count - simpleStart);
                sheet.Compounds.RemoveRange(compoundStart, sheet.Compounds.Count - compoundStart);
                sheet.Arguments.RemoveRange(argumentStart, sheet.Arguments.Count - argumentStart);
                return;
            }

            SvgSpecifiedStyle normal = default;
            SvgSpecifiedStyle important = default;
            ParseDeclarations(block, properties, ref normal, ref important);
            int normalIndex = AddStyle(sheet, in normal);
            int importantIndex = AddStyle(sheet, in important);
            int order = sheet.NextOrder();
            ReadOnlySpan<int> parsed = selectors.AsSpan();
            for (int i = 0; i < parsed.Length; i += 3)
            {
                sheet.Add(new SvgCssRule(parsed[i], parsed[i + 1], parsed[i + 2], order, normalIndex, importantIndex, isUser, media));
            }
        }
        finally
        {
            selectors.Dispose();
        }
    }

    /// <summary>
    /// Adds a declaration block to the style table when it specifies anything.
    /// </summary>
    /// <param name="sheet">The style sheet.</param>
    /// <param name="style">The block.</param>
    /// <returns>The index of the block, or -1 when it is empty.</returns>
    private static int AddStyle(SvgStyleSheet sheet, in SvgSpecifiedStyle style)
    {
        if (style.IsEmpty)
        {
            return -1;
        }

        sheet.Styles.Add(style);
        return sheet.Styles.Count - 1;
    }

    /// <summary>
    /// Parses one declaration, <c>name : value [!important]</c>, and stores it in the style for its importance.
    /// </summary>
    /// <param name="text">The declaration text, starting with the property name.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="normal">The style for normal declarations.</param>
    /// <param name="important">The style for important declarations.</param>
    private static void ParseDeclaration(ReadOnlySpan<char> text, SvgPropertyParser properties, ref SvgSpecifiedStyle normal, ref SvgSpecifiedStyle important)
    {
        // CSS Syntax 5.4.6, consume a declaration.
        SvgCssTokenizer tokenizer = new(text);
        SvgCssToken name = tokenizer.Next();
        ReadOnlySpan<char> propertyName = name.HasEscape ? name.GetValue(text) : name.GetRawValue(text);
        bool known = SvgPropertyParser.TryGetProperty(propertyName, out SvgPropertyId property);

        // The marker shorthand is not a property of its own: it sets the three marker longhands.
        bool isMarker = !known && SvgPropertyParser.IsMarkerShorthand(propertyName);
        known |= isMarker;

        SvgCssToken token = tokenizer.Next();
        while (token.Kind == SvgCssTokenKind.Whitespace)
        {
            token = tokenizer.Next();
        }

        if (!known || token.Kind != SvgCssTokenKind.Colon)
        {
            return;
        }

        // Track the last two tokens that are not whitespace to find a trailing "!important".
        int valueStart = -1;
        int valueEnd = -1;
        SvgCssToken last = default;
        SvgCssToken beforeLast = default;
        int lastCount = 0;
        bool hasGap = false;
        bool hasEscape = false;
        int previousEnd = token.End;
        while (true)
        {
            token = tokenizer.Next();
            if (token.Kind == SvgCssTokenKind.EndOfFile)
            {
                break;
            }

            // The tokenizer skips comments, so a gap between tokens means a comment was there.
            hasGap |= token.Start != previousEnd && valueStart >= 0;
            previousEnd = token.End;
            if (token.Kind == SvgCssTokenKind.Whitespace)
            {
                continue;
            }

            if (valueStart < 0)
            {
                valueStart = token.Start;
            }

            hasEscape |= token.HasEscape;
            beforeLast = last;
            last = token;
            lastCount++;
            valueEnd = token.End;
        }

        bool isImportant = lastCount >= 2
            && last.Kind == SvgCssTokenKind.Ident
            && Ascii.EqualsIgnoreCase(last.HasEscape ? last.GetValue(text) : last.GetRawValue(text), "important")
            && beforeLast.IsDelimiter(text, '!');

        if (isImportant)
        {
            valueEnd = beforeLast.Start;
        }

        if (valueStart < 0 || valueEnd <= valueStart)
        {
            return;
        }

        ReadOnlySpan<char> value = text[valueStart..valueEnd];
        ref SvgSpecifiedStyle target = ref isImportant ? ref important : ref normal;
        if (!hasGap && !hasEscape)
        {
            _ = isMarker ? properties.TryParseMarkerShorthand(value, ref target) : properties.TryParse(property, value, ref target);
            return;
        }

        // A comment or an escape inside the value: rebuild the value with each comment replaced by a space and each
        // escape decoded, so the value parsers, which know neither, see the tokens alone.
        ValueStringBuilder builder = new(stackalloc char[128]);
        try
        {
            SvgCssTokenizer valueTokens = new(value);
            int end = 0;
            for (SvgCssToken t = valueTokens.Next(); t.Kind != SvgCssTokenKind.EndOfFile; t = valueTokens.Next())
            {
                if (t.Start != end)
                {
                    builder.Append(' ');
                }

                AppendDecoded(ref builder, value, t);
                end = t.End;
            }

            _ = isMarker
                ? properties.TryParseMarkerShorthand(builder.AsSpan(), ref target)
                : properties.TryParse(property, builder.AsSpan(), ref target);
        }
        finally
        {
            builder.Dispose();
        }
    }

    /// <summary>
    /// Appends a token to a rebuilt value, with its escapes decoded.
    /// </summary>
    /// <remarks>
    /// A decoded value is appended only when it keeps its meaning without escapes: an identifier must still read as one
    /// identifier, a string must not contain its quote, and a URL must not contain characters that end it. Otherwise the
    /// raw token is appended, which the value parsers reject, as an escaped character that changes the token can never
    /// form a valid SVG property value.
    /// </remarks>
    /// <param name="builder">The builder.</param>
    /// <param name="value">The value text.</param>
    /// <param name="token">The token.</param>
    private static void AppendDecoded(ref ValueStringBuilder builder, ReadOnlySpan<char> value, SvgCssToken token)
    {
        if (token.HasEscape)
        {
            string decoded = token.GetValue(value);
            switch (token.Kind)
            {
                case SvgCssTokenKind.Ident when IsPlainName(decoded, true):
                    builder.Append(decoded);
                    return;
                case SvgCssTokenKind.Function when IsPlainName(decoded, true):
                    builder.Append(decoded);
                    builder.Append('(');
                    return;
                case SvgCssTokenKind.Hash when IsPlainName(decoded, false):
                    builder.Append('#');
                    builder.Append(decoded);
                    return;
                case SvgCssTokenKind.Dimension when IsPlainName(decoded, true) && (decoded[0] | 0x20) != 'e':
                    builder.Append(value[token.Start..token.ValueStart]);
                    builder.Append(decoded);
                    return;
                case SvgCssTokenKind.String when decoded.AsSpan().IndexOfAny(value[token.Start], '\\', '\n') < 0:
                    builder.Append(value[token.Start]);
                    builder.Append(decoded);
                    builder.Append(value[token.Start]);
                    return;
                case SvgCssTokenKind.Url when decoded.AsSpan().IndexOfAny(UrlTerminators) < 0:
                    builder.Append("url(");
                    builder.Append(decoded);
                    builder.Append(')');
                    return;
            }
        }

        builder.Append(value[token.Start..token.End]);
    }

    /// <summary>
    /// Returns whether a decoded name reads as the same name without escapes: it has only name characters and, for an
    /// identifier, does not start like a number.
    /// </summary>
    /// <param name="name">The decoded name.</param>
    /// <param name="identifier">Whether the name must start an identifier.</param>
    /// <returns><see langword="true"/> if the name needs no escape; otherwise, <see langword="false"/>.</returns>
    private static bool IsPlainName(string name, bool identifier)
    {
        if (name.Length == 0)
        {
            return false;
        }

        foreach (char c in name)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' || c >= 0x80))
            {
                return false;
            }
        }

        if (!identifier)
        {
            return true;
        }

        char first = name[0];
        char second = name.Length > 1 ? name[1] : '\0';
        return !char.IsAsciiDigit(first) && !(first == '-' && (char.IsAsciiDigit(second) || name.Length == 1));
    }

    /// <summary>
    /// Skips the rest of an at-rule after its keyword: up to and including a <c>;</c>, or a <c>{}</c> block.
    /// </summary>
    /// <param name="tokenizer">The tokenizer, positioned after the at-keyword.</param>
    private static void SkipAtRule(ref SvgCssTokenizer tokenizer)
    {
        // CSS Syntax 5.4.2, consume an at-rule.
        while (true)
        {
            SvgCssToken token = tokenizer.Next();
            switch (token.Kind)
            {
                case SvgCssTokenKind.EndOfFile:
                case SvgCssTokenKind.Semicolon:
                    return;
                case SvgCssTokenKind.OpenCurly:
                    _ = SkipBlock(ref tokenizer, SvgCssTokenKind.CloseCurly, 0);
                    return;
                default:
                    SkipComponentValue(ref tokenizer, token);
                    break;
            }
        }
    }

    /// <summary>
    /// Skips component values up to the next top-level <c>;</c> or the end of the input.
    /// </summary>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="token">The first token, already read.</param>
    /// <param name="length">The input length, returned when the input ends first.</param>
    /// <returns>The index of the <c>;</c>, or <paramref name="length"/>.</returns>
    private static int SkipToSemicolon(ref SvgCssTokenizer tokenizer, SvgCssToken token, int length)
    {
        while (true)
        {
            switch (token.Kind)
            {
                case SvgCssTokenKind.EndOfFile:
                    return length;
                case SvgCssTokenKind.Semicolon:
                    return token.Start;
                default:
                    SkipComponentValue(ref tokenizer, token);
                    break;
            }

            token = tokenizer.Next();
        }
    }

    /// <summary>
    /// Finishes consuming a component value whose first token has been read: a token that opens a block or a function
    /// consumes everything up to its matching closing token.
    /// </summary>
    /// <param name="tokenizer">The tokenizer.</param>
    /// <param name="token">The first token of the component value.</param>
    private static void SkipComponentValue(ref SvgCssTokenizer tokenizer, SvgCssToken token)
    {
        SvgCssTokenKind closing = ClosingKind(token.Kind);
        if (closing != SvgCssTokenKind.EndOfFile)
        {
            _ = SkipBlock(ref tokenizer, closing, 0);
        }
    }

    /// <summary>
    /// Skips the content of a block whose opening token has been read, through its matching closing token. Nested blocks
    /// are tracked on an explicit stack rather than by recursion.
    /// </summary>
    /// <param name="tokenizer">The tokenizer, positioned after the opening token.</param>
    /// <param name="closing">The closing token kind of the block.</param>
    /// <param name="length">The value returned when the input ends before the block closes.</param>
    /// <returns>The index of the closing token, or <paramref name="length"/>.</returns>
    private static int SkipBlock(ref SvgCssTokenizer tokenizer, SvgCssTokenKind closing, int length)
    {
        Span<SvgCssTokenKind> storage = stackalloc SvgCssTokenKind[32];
        ValueListBuilder<SvgCssTokenKind> stack = new(storage);
        try
        {
            stack.Append(closing);
            while (true)
            {
                SvgCssToken token = tokenizer.Next();
                if (token.Kind == SvgCssTokenKind.EndOfFile)
                {
                    return length;
                }

                if (token.Kind == stack[stack.Length - 1])
                {
                    if (stack.Length == 1)
                    {
                        return token.Start;
                    }

                    stack.Truncate(stack.Length - 1);
                    continue;
                }

                SvgCssTokenKind nested = ClosingKind(token.Kind);
                if (nested != SvgCssTokenKind.EndOfFile)
                {
                    stack.Append(nested);
                }
            }
        }
        finally
        {
            stack.Dispose();
        }
    }

    /// <summary>
    /// Returns the closing token kind for a token that opens a block or a function.
    /// </summary>
    /// <param name="kind">The token kind.</param>
    /// <returns>The closing kind, or <see cref="SvgCssTokenKind.EndOfFile"/> when the token opens nothing.</returns>
    private static SvgCssTokenKind ClosingKind(SvgCssTokenKind kind) => kind switch
    {
        SvgCssTokenKind.OpenCurly => SvgCssTokenKind.CloseCurly,
        SvgCssTokenKind.OpenSquare => SvgCssTokenKind.CloseSquare,
        SvgCssTokenKind.OpenParenthesis or SvgCssTokenKind.Function => SvgCssTokenKind.CloseParenthesis,
        _ => SvgCssTokenKind.EndOfFile
    };
}
