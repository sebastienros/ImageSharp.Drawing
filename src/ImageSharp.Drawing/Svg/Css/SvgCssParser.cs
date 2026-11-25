// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses style sheets and declaration lists with the parser algorithms of CSS Syntax Level 3, section 5, and the phase 1
/// selector subset.
/// </summary>
/// <remarks>
/// <para>
/// The parser streams over <see cref="SvgCssTokenizer"/> tokens and builds no component value tree. Blocks that it does
/// not use (at-rules, and nested blocks inside preludes and values) are skipped with a stack of the expected closing
/// tokens, so input such as unbalanced brackets or deep nesting cannot overflow the call stack and the parser stays in
/// sync. The recovery rules follow the CSS Syntax parser of the Jint project (BSD 2-Clause License, see
/// THIRD-PARTY-NOTICES).
/// </para>
/// <para>
/// Phase 1 selectors are type, universal, class, and id selectors, compound selectors, the descendant and child
/// combinators, and selector lists. A selector list with any other selector drops the whole rule, as CSS requires.
/// </para>
/// </remarks>
internal static class SvgCssParser
{
    /// <summary>
    /// Parses a style sheet and appends its rules to <paramref name="sheet"/>.
    /// </summary>
    /// <param name="text">The style sheet text.</param>
    /// <param name="sheet">The style sheet that receives the rules.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="names">The pool that interns class names.</param>
    public static void ParseStyleSheet(ReadOnlySpan<char> text, SvgStyleSheet sheet, SvgPropertyParser properties, SvgStringPool names)
    {
        // CSS Syntax 5.4.1, consume a list of rules, with the top-level flag set.
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
                    // Phase 1 supports no at-rule. Skipping it whole drops @media and @import content.
                    SkipAtRule(ref tokenizer);
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
            AddRule(text[preludeStart..preludeEnd], text[blockStart..blockEnd], sheet, properties, names);
        }
    }

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
    /// Parses the selector list of a rule and, when it is valid, adds one rule per selector.
    /// </summary>
    /// <param name="prelude">The selector list text.</param>
    /// <param name="block">The declaration block text, without braces.</param>
    /// <param name="sheet">The style sheet that receives the rules.</param>
    /// <param name="properties">The property parser.</param>
    /// <param name="names">The pool that interns class names.</param>
    private static void AddRule(ReadOnlySpan<char> prelude, ReadOnlySpan<char> block, SvgStyleSheet sheet, SvgPropertyParser properties, SvgStringPool names)
    {
        int simpleStart = sheet.SimpleSelectors.Count;
        int compoundStart = sheet.Compounds.Count;
        Span<int> selectorStorage = stackalloc int[16];
        ValueListBuilder<int> selectors = new(selectorStorage);
        try
        {
            // Each selector is stored as (first compound, compound count, specificity) in the builder.
            if (!TryParseSelectorList(prelude, sheet, names, ref selectors))
            {
                sheet.SimpleSelectors.RemoveRange(simpleStart, sheet.SimpleSelectors.Count - simpleStart);
                sheet.Compounds.RemoveRange(compoundStart, sheet.Compounds.Count - compoundStart);
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
                sheet.Add(new SvgCssRule(parsed[i], parsed[i + 1], parsed[i + 2], order, normalIndex, importantIndex));
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
    /// Parses a selector list into the style sheet tables.
    /// </summary>
    /// <param name="prelude">The selector list text.</param>
    /// <param name="sheet">The style sheet that receives the simple and compound selectors.</param>
    /// <param name="names">The pool that interns class names.</param>
    /// <param name="selectors">The builder that receives (first compound, compound count, specificity) per selector.</param>
    /// <returns><see langword="true"/> if every selector in the list is supported and valid; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseSelectorList(ReadOnlySpan<char> prelude, SvgStyleSheet sheet, SvgStringPool names, ref ValueListBuilder<int> selectors)
    {
        SvgCssTokenizer tokenizer = new(prelude);
        int firstCompound = sheet.Compounds.Count;
        int ids = 0;
        int classes = 0;
        int types = 0;

        // The compound being read: its first simple selector, or -1 when no compound is open. A compound closes at
        // whitespace or a combinator.
        int openCompound = -1;
        SvgCssCombinator combinator = SvgCssCombinator.None;
        while (true)
        {
            SvgCssToken token = tokenizer.Next();
            SvgCssSimpleSelectorKind kind;
            string? name;
            switch (token.Kind)
            {
                case SvgCssTokenKind.Whitespace:
                    if (openCompound >= 0)
                    {
                        CloseCompound(sheet, ref openCompound, combinator);
                        combinator = SvgCssCombinator.Descendant;
                    }

                    continue;
                case SvgCssTokenKind.Comma:
                case SvgCssTokenKind.EndOfFile:
                    if (openCompound >= 0)
                    {
                        CloseCompound(sheet, ref openCompound, combinator);
                    }
                    else if (combinator == SvgCssCombinator.Child)
                    {
                        // A combinator needs a compound on its right.
                        return false;
                    }

                    int count = sheet.Compounds.Count - firstCompound;
                    if (count is 0 or > SvgStyleSheet.MaxCompoundsPerSelector)
                    {
                        return false;
                    }

                    selectors.Append(firstCompound);
                    selectors.Append(count);
                    selectors.Append((Math.Min(ids, 255) << 16) | (Math.Min(classes, 255) << 8) | Math.Min(types, 255));
                    if (token.Kind == SvgCssTokenKind.EndOfFile)
                    {
                        return true;
                    }

                    firstCompound = sheet.Compounds.Count;
                    ids = classes = types = 0;
                    combinator = SvgCssCombinator.None;
                    continue;
                case SvgCssTokenKind.Delim when token.IsDelimiter(prelude, '>'):
                    if (openCompound >= 0)
                    {
                        CloseCompound(sheet, ref openCompound, combinator);
                    }
                    else if (combinator != SvgCssCombinator.Descendant)
                    {
                        // ">" needs a compound on its left, and two combinators cannot follow each other.
                        return false;
                    }

                    combinator = SvgCssCombinator.Child;
                    continue;
                case SvgCssTokenKind.Ident:
                    kind = SvgCssSimpleSelectorKind.Type;
                    name = token.GetValue(prelude);
                    types++;
                    break;
                case SvgCssTokenKind.Delim when token.IsDelimiter(prelude, '*'):
                    kind = SvgCssSimpleSelectorKind.Universal;
                    name = null;
                    break;
                case SvgCssTokenKind.Hash when token.IsIdHash:
                    kind = SvgCssSimpleSelectorKind.Id;
                    name = token.GetValue(prelude);
                    ids++;
                    break;
                case SvgCssTokenKind.Delim when token.IsDelimiter(prelude, '.'):
                    // The class name must follow the dot directly.
                    SvgCssToken className = tokenizer.Next();
                    if (className.Kind != SvgCssTokenKind.Ident)
                    {
                        return false;
                    }

                    kind = SvgCssSimpleSelectorKind.Class;
                    name = className.HasEscape ? names.Get(className.GetValue(prelude)) : names.Get(className.GetRawValue(prelude));
                    classes++;
                    break;
                default:
                    // Attribute selectors, pseudo-classes, sibling combinators, namespaces, and anything else are not
                    // phase 1 selectors.
                    return false;
            }

            // A type or universal selector must come first in its compound.
            if (kind is SvgCssSimpleSelectorKind.Type or SvgCssSimpleSelectorKind.Universal && openCompound >= 0)
            {
                return false;
            }

            if (openCompound < 0)
            {
                openCompound = sheet.SimpleSelectors.Count;
            }

            sheet.SimpleSelectors.Add(new SvgCssSimpleSelector(kind, name));
        }
    }

    /// <summary>
    /// Closes the open compound selector and adds it to the compound table.
    /// </summary>
    /// <param name="sheet">The style sheet.</param>
    /// <param name="openCompound">The first simple selector of the open compound; set to -1.</param>
    /// <param name="combinator">The combinator before the compound.</param>
    private static void CloseCompound(SvgStyleSheet sheet, ref int openCompound, SvgCssCombinator combinator)
    {
        sheet.Compounds.Add(new SvgCssCompoundSelector(openCompound, sheet.SimpleSelectors.Count - openCompound, combinator));
        openCompound = -1;
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

            beforeLast = last;
            last = token;
            lastCount++;
            valueEnd = token.End;
        }

        bool isImportant = lastCount >= 2
            && last.Kind == SvgCssTokenKind.Ident
            && last.GetRawValue(text).Equals("important", StringComparison.OrdinalIgnoreCase)
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
        if (!hasGap)
        {
            _ = isMarker ? properties.TryParseMarkerShorthand(value, ref target) : properties.TryParse(property, value, ref target);
            return;
        }

        // A comment inside the value: rebuild the value with each comment replaced by a space, so the value parsers,
        // which do not know CSS comments, see the tokens alone.
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

                builder.Append(value[t.Start..t.End]);
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
