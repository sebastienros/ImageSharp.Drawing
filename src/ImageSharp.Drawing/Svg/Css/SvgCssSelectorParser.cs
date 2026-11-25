// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Parses selector lists (Selectors Level 4) into the flat tables of a <see cref="SvgStyleSheet"/>.
/// </summary>
/// <remarks>
/// <para>
/// The grammar, the error rules, the An+B parser, and the specificity rules follow the selector compiler of the Jint
/// project (<c>Jint.HtmlParser/Css/Selectors/SelectorCompiler.cs</c>, BSD 2-Clause License, see THIRD-PARTY-NOTICES),
/// adapted to work on the token ranges of <see cref="SvgCssTokenizer"/> instead of a component value tree, to write flat
/// tables instead of objects, and to leave out what an SVG document cannot use: namespace declarations, nesting,
/// <c>:has()</c>, shadow trees, columns, and language and direction pseudo-classes. A selector that uses one of them is
/// invalid, so its rule is dropped.
/// </para>
/// <para>
/// Supported: type, universal, id, class, and attribute selectors (all operators, the <c>i</c> and <c>s</c> flags, the
/// <c>*|</c> and <c>|</c> attribute prefixes); the four combinators; <c>:root</c>, <c>:scope</c>, <c>:empty</c>,
/// <c>:defined</c>, the child-indexed and typed-child-indexed pseudo-classes including <c>of S</c>; <c>:not()</c>,
/// <c>:is()</c>, and <c>:where()</c>. Pseudo-elements and the pseudo-classes of interaction, link, and form state parse as
/// valid selectors that never match.
/// </para>
/// <para>
/// One invalid selector invalidates its whole list, except in the forgiving lists of <c>:is()</c> and <c>:where()</c>,
/// where only the invalid selector is dropped. Functional pseudo-classes nest at most
/// <see cref="SvgStyleSheet.MaxSelectorNesting"/> levels deep. A deeper selector makes its whole rule invalid, also inside
/// a forgiving list, so the limit never changes what a rule that is kept matches. Recursion is bounded by that limit, and
/// every token is visited a bounded number of times, so parsing is linear in the prelude length.
/// </para>
/// <para>
/// Tables: the simple selectors of a compound are contiguous, the compounds of a complex selector are contiguous, and the
/// selectors of one argument list are contiguous. Argument lists are parsed before the compound that holds them closes,
/// so each level collects its own entries in scratch storage and copies them to the style sheet when it completes.
/// </para>
/// </remarks>
internal ref struct SvgCssSelectorParser : IDisposable
{
    private readonly ReadOnlySpan<char> source;
    private readonly ReadOnlySpan<SvgCssToken> tokens;
    private readonly ReadOnlySpan<int> closes;
    private readonly SvgStyleSheet sheet;
    private readonly SvgStringPool names;
    private readonly List<SvgCssSimpleSelector> scratchSimples;
    private ValueListBuilder<SvgCssCompoundSelector> scratchCompounds;
    private ValueListBuilder<int> scratchSelectors;
    private bool nestingExceeded;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssSelectorParser"/> struct.
    /// </summary>
    /// <param name="source">The selector text.</param>
    /// <param name="tokens">The tokens of the text.</param>
    /// <param name="closes">For each token that opens a block, the index of its closing token, or the token count.</param>
    /// <param name="sheet">The style sheet that receives the tables.</param>
    /// <param name="names">The pool that interns class and attribute names.</param>
    /// <param name="compoundStorage">The initial storage of the compound scratch stack.</param>
    /// <param name="selectorStorage">The initial storage of the selector scratch stack.</param>
    private SvgCssSelectorParser(
        ReadOnlySpan<char> source,
        ReadOnlySpan<SvgCssToken> tokens,
        ReadOnlySpan<int> closes,
        SvgStyleSheet sheet,
        SvgStringPool names,
        Span<SvgCssCompoundSelector> compoundStorage,
        Span<int> selectorStorage)
    {
        this.source = source;
        this.tokens = tokens;
        this.closes = closes;
        this.sheet = sheet;
        this.names = names;
        this.scratchSimples = sheet.ScratchSimples;
        this.scratchCompounds = new ValueListBuilder<SvgCssCompoundSelector>(compoundStorage);
        this.scratchSelectors = new ValueListBuilder<int>(selectorStorage);
    }

    /// <summary>
    /// Parses the selector list of a style rule.
    /// </summary>
    /// <param name="prelude">The selector list text.</param>
    /// <param name="sheet">The style sheet that receives the simple, compound, and argument selectors.</param>
    /// <param name="names">The pool that interns class and attribute names.</param>
    /// <param name="selectors">The builder that receives (first compound, compound count, specificity) per selector.</param>
    /// <returns><see langword="true"/> if every selector in the list is valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryParse(ReadOnlySpan<char> prelude, SvgStyleSheet sheet, SvgStringPool names, ref ValueListBuilder<int> selectors)
    {
        ValueListBuilder<SvgCssToken> tokens = new(stackalloc SvgCssToken[32]);
        ValueListBuilder<int> closes = new(stackalloc int[32]);
        ValueListBuilder<int> open = new(stackalloc int[8]);
        try
        {
            // Tokenize once and pair every block opener with its closer, so the recursive descent below jumps over
            // nested blocks in constant time. A block that the input does not close ends at the end of the input, as
            // CSS Syntax closes blocks at the end of the input.
            SvgCssTokenizer tokenizer = new(prelude);
            for (SvgCssToken token = tokenizer.Next(); token.Kind != SvgCssTokenKind.EndOfFile; token = tokenizer.Next())
            {
                int index = tokens.Length;
                tokens.Append(token);
                closes.Append(-1);
                switch (token.Kind)
                {
                    case SvgCssTokenKind.OpenParenthesis:
                    case SvgCssTokenKind.Function:
                    case SvgCssTokenKind.OpenSquare:
                    case SvgCssTokenKind.OpenCurly:
                        open.Append(index);
                        break;
                    case SvgCssTokenKind.CloseParenthesis:
                    case SvgCssTokenKind.CloseSquare:
                    case SvgCssTokenKind.CloseCurly:
                        if (open.Length > 0 && Closes(tokens[open[open.Length - 1]].Kind, token.Kind))
                        {
                            closes[open[open.Length - 1]] = index;
                            open.Truncate(open.Length - 1);
                        }

                        break;
                }
            }

            for (int i = 0; i < open.Length; i++)
            {
                closes[open[i]] = tokens.Length;
            }

            SvgCssSelectorParser parser = new(
                prelude,
                tokens.AsSpan(),
                closes.AsSpan(),
                sheet,
                names,
                stackalloc SvgCssCompoundSelector[16],
                stackalloc int[24]);
            try
            {
                if (!parser.TryParseList(0, tokens.Length, 0, false, true, out _, out _))
                {
                    return false;
                }

                foreach (int value in parser.scratchSelectors.AsSpan())
                {
                    selectors.Append(value);
                }

                return true;
            }
            finally
            {
                parser.Dispose();
                sheet.ScratchSimples.Clear();
            }
        }
        finally
        {
            tokens.Dispose();
            closes.Dispose();
            open.Dispose();
        }
    }

    /// <summary>
    /// Returns the pooled storage of the scratch stacks.
    /// </summary>
    public void Dispose()
    {
        this.scratchCompounds.Dispose();
        this.scratchSelectors.Dispose();
    }

    /// <summary>
    /// Returns whether a closing token kind closes a block that an opening token kind opened.
    /// </summary>
    /// <param name="opening">The opening token kind.</param>
    /// <param name="closing">The closing token kind.</param>
    /// <returns><see langword="true"/> if the tokens pair; otherwise, <see langword="false"/>.</returns>
    private static bool Closes(SvgCssTokenKind opening, SvgCssTokenKind closing) => opening switch
    {
        SvgCssTokenKind.OpenParenthesis or SvgCssTokenKind.Function => closing == SvgCssTokenKind.CloseParenthesis,
        SvgCssTokenKind.OpenSquare => closing == SvgCssTokenKind.CloseSquare,
        _ => closing == SvgCssTokenKind.CloseCurly
    };

    /// <summary>
    /// Packs a specificity, saturating each count at 255.
    /// </summary>
    /// <param name="ids">The id count.</param>
    /// <param name="classes">The class, attribute, and pseudo-class count.</param>
    /// <param name="types">The type and pseudo-element count.</param>
    /// <returns>The packed specificity.</returns>
    private static int PackSpecificity(int ids, int classes, int types)
        => (Math.Min(ids, 255) << 16) | (Math.Min(classes, 255) << 8) | Math.Min(types, 255);

    /// <summary>
    /// Returns whether a name equals an ASCII keyword, ignoring ASCII case, as CSS compares keywords.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="keyword">The lowercase keyword.</param>
    /// <returns><see langword="true"/> if the name equals the keyword; otherwise, <see langword="false"/>.</returns>
    private static bool IsKeyword(ReadOnlySpan<char> name, string keyword) => Ascii.EqualsIgnoreCase(name, keyword);

    /// <summary>
    /// Returns whether a pseudo-class name is one that needs interaction, link, or form state, so it never matches in a
    /// static document.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> for such a pseudo-class; otherwise, <see langword="false"/>.</returns>
    private static bool IsStatePseudoClass(ReadOnlySpan<char> name)
        => IsKeyword(name, "hover") || IsKeyword(name, "active") || IsKeyword(name, "focus")
        || IsKeyword(name, "focus-within") || IsKeyword(name, "focus-visible")
        || IsKeyword(name, "target") || IsKeyword(name, "visited") || IsKeyword(name, "link")
        || IsKeyword(name, "any-link") || IsKeyword(name, "checked") || IsKeyword(name, "indeterminate")
        || IsKeyword(name, "default") || IsKeyword(name, "enabled") || IsKeyword(name, "disabled")
        || IsKeyword(name, "required") || IsKeyword(name, "optional") || IsKeyword(name, "valid")
        || IsKeyword(name, "invalid") || IsKeyword(name, "in-range") || IsKeyword(name, "out-of-range")
        || IsKeyword(name, "read-only") || IsKeyword(name, "read-write") || IsKeyword(name, "placeholder-shown")
        || IsKeyword(name, "autofill") || IsKeyword(name, "open") || IsKeyword(name, "closed")
        || IsKeyword(name, "fullscreen") || IsKeyword(name, "modal");

    /// <summary>
    /// Returns whether a name is a pseudo-element that may be written with one colon, as CSS 2 defined it.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> for such a pseudo-element; otherwise, <see langword="false"/>.</returns>
    private static bool IsLegacyPseudoElement(ReadOnlySpan<char> name)
        => IsKeyword(name, "before") || IsKeyword(name, "after") || IsKeyword(name, "first-line") || IsKeyword(name, "first-letter");

    /// <summary>
    /// Returns whether a name is a known pseudo-element. Chromium also accepts any name with the <c>-webkit-</c> prefix.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="function">Whether the name is written as a function.</param>
    /// <returns><see langword="true"/> for a known pseudo-element; otherwise, <see langword="false"/>.</returns>
    private static bool IsPseudoElement(ReadOnlySpan<char> name, bool function)
    {
        if (function)
        {
            return IsKeyword(name, "part") || IsKeyword(name, "slotted") || IsKeyword(name, "cue") || IsKeyword(name, "highlight");
        }

        return IsLegacyPseudoElement(name)
            || IsKeyword(name, "marker") || IsKeyword(name, "placeholder") || IsKeyword(name, "selection")
            || IsKeyword(name, "backdrop") || IsKeyword(name, "file-selector-button") || IsKeyword(name, "grammar-error")
            || IsKeyword(name, "spelling-error") || IsKeyword(name, "target-text") || IsKeyword(name, "cue")
            || (name.Length > 8 && Ascii.EqualsIgnoreCase(name[..8], "-webkit-"));
    }

    /// <summary>
    /// Parses a selector list over a token range and pushes (first compound, compound count, specificity) per valid
    /// selector on the selector scratch stack, where the caller takes them from.
    /// </summary>
    /// <param name="start">The first token.</param>
    /// <param name="end">The token after the last.</param>
    /// <param name="depth">The functional pseudo-class nesting depth; 0 at the top level.</param>
    /// <param name="forgiving">Whether invalid selectors are dropped instead of invalidating the list.</param>
    /// <param name="allowPseudoElements">Whether pseudo-elements are allowed.</param>
    /// <param name="maxSpecificity">The largest specificity of the selectors.</param>
    /// <param name="isComplex">Whether a selector has a combinator, directly or nested.</param>
    /// <returns><see langword="true"/> if the list is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseList(
        int start,
        int end,
        int depth,
        bool forgiving,
        bool allowPseudoElements,
        out int maxSpecificity,
        out bool isComplex)
    {
        maxSpecificity = 0;
        isComplex = false;
        if (depth > SvgStyleSheet.MaxSelectorNesting)
        {
            this.nestingExceeded = true;
            return false;
        }

        int partStart = start;
        int i = start;
        while (true)
        {
            if (i < end && this.tokens[i].Kind != SvgCssTokenKind.Comma)
            {
                i = this.Next(i, end);
                continue;
            }

            int simpleMark = this.sheet.SimpleSelectors.Count;
            int compoundMark = this.sheet.Compounds.Count;
            int argumentMark = this.sheet.Arguments.Count;
            if (this.TryParseComplex(
                partStart,
                i,
                depth,
                allowPseudoElements,
                out int compoundStart,
                out int compoundCount,
                out int specificity,
                out bool complex))
            {
                this.scratchSelectors.Append(compoundStart);
                this.scratchSelectors.Append(compoundCount);
                this.scratchSelectors.Append(specificity);
                maxSpecificity = Math.Max(maxSpecificity, specificity);
                isComplex |= complex;
            }
            else
            {
                if (!forgiving || this.nestingExceeded)
                {
                    return false;
                }

                // Drop what the invalid selector wrote, including argument lists that it completed before failing.
                this.sheet.SimpleSelectors.RemoveRange(simpleMark, this.sheet.SimpleSelectors.Count - simpleMark);
                this.sheet.Compounds.RemoveRange(compoundMark, this.sheet.Compounds.Count - compoundMark);
                this.sheet.Arguments.RemoveRange(argumentMark, this.sheet.Arguments.Count - argumentMark);
            }

            if (i >= end)
            {
                return true;
            }

            i++;
            partStart = i;
        }
    }

    /// <summary>
    /// Returns the index after the component value that starts at a token: after the closing token of a block, or the
    /// next token.
    /// </summary>
    /// <param name="index">The token index.</param>
    /// <param name="end">The end of the current range.</param>
    /// <returns>The next index, at most <paramref name="end"/>.</returns>
    private int Next(int index, int end)
    {
        int close = this.closes[index];
        return close < 0 ? index + 1 : Math.Min(close + 1, end);
    }

    /// <summary>
    /// Returns whether a token is whitespace.
    /// </summary>
    /// <param name="index">The token index.</param>
    /// <returns><see langword="true"/> if the token is whitespace; otherwise, <see langword="false"/>.</returns>
    private bool IsWhitespace(int index) => this.tokens[index].Kind == SvgCssTokenKind.Whitespace;

    /// <summary>
    /// Returns whether a token is a delimiter with the given character.
    /// </summary>
    /// <param name="index">The token index.</param>
    /// <param name="delimiter">The character.</param>
    /// <returns><see langword="true"/> if the token is that delimiter; otherwise, <see langword="false"/>.</returns>
    private bool IsDelimiter(int index, char delimiter) => this.tokens[index].IsDelimiter(this.source, delimiter);

    /// <summary>
    /// Returns the value of an identifier-like token with escapes decoded. The common token without escapes is returned
    /// as a range of the source, so it allocates nothing.
    /// </summary>
    /// <param name="index">The token index.</param>
    /// <returns>The value.</returns>
    private ReadOnlySpan<char> Name(int index)
    {
        SvgCssToken token = this.tokens[index];
        return token.HasEscape ? token.GetValue(this.source) : token.GetRawValue(this.source);
    }

    /// <summary>
    /// Skips whitespace tokens.
    /// </summary>
    /// <param name="i">The token index; advanced past whitespace.</param>
    /// <param name="end">The end of the range.</param>
    /// <returns><see langword="true"/> if whitespace was skipped; otherwise, <see langword="false"/>.</returns>
    private bool SkipWhitespace(ref int i, int end)
    {
        int start = i;
        while (i < end && this.IsWhitespace(i))
        {
            i++;
        }

        return i != start;
    }

    /// <summary>
    /// Parses one complex selector and copies its compounds to the style sheet.
    /// </summary>
    /// <param name="start">The first token.</param>
    /// <param name="end">The token after the last.</param>
    /// <param name="depth">The nesting depth.</param>
    /// <param name="allowPseudoElements">Whether pseudo-elements are allowed.</param>
    /// <param name="compoundStart">The index of the first compound in the style sheet table.</param>
    /// <param name="compoundCount">The number of compounds.</param>
    /// <param name="specificity">The packed specificity.</param>
    /// <param name="isComplex">Whether the selector has a combinator, directly or nested.</param>
    /// <returns><see langword="true"/> if the selector is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseComplex(
        int start,
        int end,
        int depth,
        bool allowPseudoElements,
        out int compoundStart,
        out int compoundCount,
        out int specificity,
        out bool isComplex)
    {
        compoundStart = 0;
        compoundCount = 0;
        specificity = 0;
        isComplex = false;
        while (start < end && this.IsWhitespace(start))
        {
            start++;
        }

        while (end > start && this.IsWhitespace(end - 1))
        {
            end--;
        }

        if (start == end)
        {
            return false;
        }

        int scratchStart = this.scratchCompounds.Length;
        try
        {
            int ids = 0;
            int classes = 0;
            int types = 0;
            SvgCssCombinator combinator = SvgCssCombinator.None;
            int i = start;
            while (true)
            {
                if (!this.TryParseCompound(
                    ref i,
                    end,
                    depth,
                    allowPseudoElements,
                    ref ids,
                    ref classes,
                    ref types,
                    ref isComplex,
                    out int simpleStart,
                    out int simpleCount,
                    out bool hasPseudoElement))
                {
                    return false;
                }

                this.scratchCompounds.Append(new SvgCssCompoundSelector(simpleStart, simpleCount, combinator));
                if (i == end)
                {
                    break;
                }

                // A pseudo-element ends the selector: no combinator may follow it.
                bool space = this.SkipWhitespace(ref i, end);
                if (hasPseudoElement)
                {
                    return false;
                }

                if (this.IsDelimiter(i, '>'))
                {
                    combinator = SvgCssCombinator.Child;
                }
                else if (this.IsDelimiter(i, '+'))
                {
                    combinator = SvgCssCombinator.NextSibling;
                }
                else if (this.IsDelimiter(i, '~'))
                {
                    combinator = SvgCssCombinator.SubsequentSibling;
                }
                else if (space)
                {
                    combinator = SvgCssCombinator.Descendant;
                    continue;
                }
                else
                {
                    // A token that is neither part of the compound nor a combinator, such as "*" after a type.
                    return false;
                }

                // An explicit combinator needs a compound on its right.
                i++;
                _ = this.SkipWhitespace(ref i, end);
                if (i == end)
                {
                    return false;
                }
            }

            compoundCount = this.scratchCompounds.Length - scratchStart;
            if (compoundCount > SvgStyleSheet.MaxCompoundsPerSelector)
            {
                return false;
            }

            compoundStart = this.sheet.Compounds.Count;
            for (int c = scratchStart; c < this.scratchCompounds.Length; c++)
            {
                this.sheet.Compounds.Add(this.scratchCompounds[c]);
            }

            specificity = PackSpecificity(ids, classes, types);
            isComplex |= compoundCount > 1;
            return true;
        }
        finally
        {
            this.scratchCompounds.Truncate(scratchStart);
        }
    }

    /// <summary>
    /// Parses one compound selector and copies its simple selectors to the style sheet.
    /// </summary>
    /// <param name="i">The first token; advanced past the compound.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="depth">The nesting depth.</param>
    /// <param name="allowPseudoElements">Whether pseudo-elements are allowed.</param>
    /// <param name="ids">The id count of the complex selector.</param>
    /// <param name="classes">The class count of the complex selector.</param>
    /// <param name="types">The type count of the complex selector.</param>
    /// <param name="isComplex">Set when an argument list has a combinator.</param>
    /// <param name="simpleStart">The index of the first simple selector in the style sheet table.</param>
    /// <param name="simpleCount">The number of simple selectors.</param>
    /// <param name="hasPseudoElement">Whether the compound has a pseudo-element.</param>
    /// <returns><see langword="true"/> if the compound is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseCompound(
        ref int i,
        int end,
        int depth,
        bool allowPseudoElements,
        ref int ids,
        ref int classes,
        ref int types,
        ref bool isComplex,
        out int simpleStart,
        out int simpleCount,
        out bool hasPseudoElement)
    {
        simpleStart = 0;
        simpleCount = 0;
        hasPseudoElement = false;
        List<SvgCssSimpleSelector> scratch = this.scratchSimples;
        int scratchStart = scratch.Count;
        try
        {
            // An optional type or universal selector with an optional namespace prefix. Without @namespace rules only the
            // "*|" prefix, which means any namespace, is valid; it is the same as no prefix. "|rect" (no namespace) never
            // matches an SVG element, and a named prefix is undeclared, so both are invalid here.
            SvgCssToken first = this.tokens[i];
            if (first.Kind == SvgCssTokenKind.Ident || first.IsDelimiter(this.source, '*') || first.IsDelimiter(this.source, '|'))
            {
                if (first.IsDelimiter(this.source, '|'))
                {
                    return false;
                }

                if (i + 1 < end && this.IsDelimiter(i + 1, '|') && !(i + 2 < end && this.IsDelimiter(i + 2, '|')))
                {
                    if (first.Kind == SvgCssTokenKind.Ident || i + 2 >= end)
                    {
                        return false;
                    }

                    i += 2;
                    if (this.tokens[i].Kind != SvgCssTokenKind.Ident && !this.IsDelimiter(i, '*'))
                    {
                        return false;
                    }
                }

                if (this.tokens[i].Kind == SvgCssTokenKind.Ident)
                {
                    scratch.Add(new SvgCssSimpleSelector(SvgCssSimpleSelectorKind.Type, this.tokens[i].GetValue(this.source)));
                    types++;
                }
                else
                {
                    scratch.Add(new SvgCssSimpleSelector(SvgCssSimpleSelectorKind.Universal, null));
                }

                i++;
            }

            while (i < end)
            {
                SvgCssToken token = this.tokens[i];
                if (hasPseudoElement && token.Kind != SvgCssTokenKind.Colon)
                {
                    break;
                }

                switch (token.Kind)
                {
                    case SvgCssTokenKind.Hash:
                        if (!token.IsIdHash)
                        {
                            return false;
                        }

                        scratch.Add(new SvgCssSimpleSelector(SvgCssSimpleSelectorKind.Id, token.GetValue(this.source)));
                        ids++;
                        i++;
                        continue;

                    case SvgCssTokenKind.Delim when token.IsDelimiter(this.source, '.'):
                        // The class name must follow the dot directly.
                        if (i + 1 >= end || this.tokens[i + 1].Kind != SvgCssTokenKind.Ident)
                        {
                            return false;
                        }

                        scratch.Add(new SvgCssSimpleSelector(SvgCssSimpleSelectorKind.Class, this.names.Get(this.Name(i + 1))));
                        classes++;
                        i += 2;
                        continue;

                    case SvgCssTokenKind.OpenSquare:
                    {
                        int close = this.closes[i];
                        if (close >= end || !this.TryParseAttribute(i + 1, close, out SvgCssSimpleSelector attribute))
                        {
                            return false;
                        }

                        scratch.Add(attribute);
                        classes++;
                        i = close + 1;
                        continue;
                    }

                    case SvgCssTokenKind.Colon:
                        if (!this.TryParsePseudo(ref i, end, depth, allowPseudoElements, ref ids, ref classes, ref types, ref isComplex, ref hasPseudoElement))
                        {
                            return false;
                        }

                        continue;
                }

                break;
            }

            if (scratch.Count == scratchStart)
            {
                return false;
            }

            simpleStart = this.sheet.SimpleSelectors.Count;
            simpleCount = scratch.Count - scratchStart;
            for (int s = scratchStart; s < scratch.Count; s++)
            {
                this.sheet.SimpleSelectors.Add(scratch[s]);
            }

            return true;
        }
        finally
        {
            scratch.RemoveRange(scratchStart, scratch.Count - scratchStart);
        }
    }

    /// <summary>
    /// Parses a pseudo-class or pseudo-element that starts with a colon token.
    /// </summary>
    /// <param name="i">The index of the colon; advanced past the selector.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="depth">The nesting depth.</param>
    /// <param name="allowPseudoElements">Whether pseudo-elements are allowed.</param>
    /// <param name="ids">The id count of the complex selector.</param>
    /// <param name="classes">The class count of the complex selector.</param>
    /// <param name="types">The type count of the complex selector.</param>
    /// <param name="isComplex">Set when an argument list has a combinator.</param>
    /// <param name="hasPseudoElement">Whether the compound already has a pseudo-element; set when this is one.</param>
    /// <returns><see langword="true"/> if the selector is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParsePseudo(
        ref int i,
        int end,
        int depth,
        bool allowPseudoElements,
        ref int ids,
        ref int classes,
        ref int types,
        ref bool isComplex,
        ref bool hasPseudoElement)
    {
        List<SvgCssSimpleSelector> scratch = this.scratchSimples;
        bool element = i + 1 < end && this.tokens[i + 1].Kind == SvgCssTokenKind.Colon;
        int nameIndex = i + (element ? 2 : 1);
        if (nameIndex >= end)
        {
            return false;
        }

        SvgCssToken nameToken = this.tokens[nameIndex];
        bool function = nameToken.Kind == SvgCssTokenKind.Function;
        if (!function && nameToken.Kind != SvgCssTokenKind.Ident)
        {
            return false;
        }

        ReadOnlySpan<char> name = this.Name(nameIndex);

        // Nothing may follow a pseudo-element in its compound: Chromium drops "::before:hover" too.
        if (hasPseudoElement)
        {
            return false;
        }

        if (element || (!function && IsLegacyPseudoElement(name)))
        {
            if (!allowPseudoElements || !IsPseudoElement(name, function))
            {
                return false;
            }

            scratch.Add(new SvgCssSimpleSelector(SvgCssSimpleSelectorKind.Never, null));
            types++;
            hasPseudoElement = true;
            i = function ? this.closes[nameIndex] + 1 : nameIndex + 1;
            return i <= end;
        }

        if (!function)
        {
            SvgCssSimpleSelectorKind kind;
            if (IsKeyword(name, "root") || IsKeyword(name, "scope"))
            {
                kind = SvgCssSimpleSelectorKind.Root;
            }
            else if (IsKeyword(name, "empty"))
            {
                kind = SvgCssSimpleSelectorKind.Empty;
                this.sheet.UsesEmpty = true;
            }
            else if (IsKeyword(name, "first-child"))
            {
                kind = SvgCssSimpleSelectorKind.FirstChild;
            }
            else if (IsKeyword(name, "last-child"))
            {
                kind = SvgCssSimpleSelectorKind.LastChild;
            }
            else if (IsKeyword(name, "only-child"))
            {
                kind = SvgCssSimpleSelectorKind.OnlyChild;
            }
            else if (IsKeyword(name, "first-of-type"))
            {
                kind = SvgCssSimpleSelectorKind.FirstOfType;
            }
            else if (IsKeyword(name, "last-of-type"))
            {
                kind = SvgCssSimpleSelectorKind.LastOfType;
            }
            else if (IsKeyword(name, "only-of-type"))
            {
                kind = SvgCssSimpleSelectorKind.OnlyOfType;
            }
            else if (IsKeyword(name, "defined"))
            {
                kind = SvgCssSimpleSelectorKind.Universal;
            }
            else if (IsStatePseudoClass(name))
            {
                kind = SvgCssSimpleSelectorKind.Never;
            }
            else
            {
                // Selectors Level 4, section 3.9: an unknown pseudo-class makes the selector invalid.
                return false;
            }

            scratch.Add(new SvgCssSimpleSelector(kind, null));
            classes++;
            i = nameIndex + 1;
            return true;
        }

        int argumentStart = nameIndex + 1;
        int close = this.closes[nameIndex];
        if (close >= end)
        {
            return false;
        }

        int listStart = this.scratchSelectors.Length;
        try
        {
            int maxSpecificity;
            bool complex;
            if (IsKeyword(name, "not") || IsKeyword(name, "is") || IsKeyword(name, "where"))
            {
                SvgCssSimpleSelectorKind kind = IsKeyword(name, "not") ? SvgCssSimpleSelectorKind.Not
                    : IsKeyword(name, "is") ? SvgCssSimpleSelectorKind.Is
                    : SvgCssSimpleSelectorKind.Where;

                // :is() and :where() take a forgiving list; :not() does not.
                if (!this.TryParseList(argumentStart, close, depth + 1, kind != SvgCssSimpleSelectorKind.Not, false, out maxSpecificity, out complex))
                {
                    return false;
                }

                scratch.Add(new SvgCssSimpleSelector(kind, null, this.AddFunctional(0, 0, listStart, complex)));

                // Selectors Level 4, section 17: :is() and :not() take the specificity of their most specific argument;
                // :where() adds nothing.
                if (kind != SvgCssSimpleSelectorKind.Where)
                {
                    AddSpecificity(maxSpecificity, ref ids, ref classes, ref types);
                }
            }
            else
            {
                SvgCssSimpleSelectorKind kind;
                if (IsKeyword(name, "nth-child"))
                {
                    kind = SvgCssSimpleSelectorKind.NthChild;
                }
                else if (IsKeyword(name, "nth-last-child"))
                {
                    kind = SvgCssSimpleSelectorKind.NthLastChild;
                }
                else if (IsKeyword(name, "nth-of-type"))
                {
                    kind = SvgCssSimpleSelectorKind.NthOfType;
                }
                else if (IsKeyword(name, "nth-last-of-type"))
                {
                    kind = SvgCssSimpleSelectorKind.NthLastOfType;
                }
                else
                {
                    // :has(), :lang(), :dir(), :host(), and unknown functions.
                    return false;
                }

                // "of S" is allowed only for the child-indexed forms.
                int of = close;
                for (int t = argumentStart; t < close; t = this.Next(t, close))
                {
                    if (this.tokens[t].Kind == SvgCssTokenKind.Ident && IsKeyword(this.Name(t), "of"))
                    {
                        of = t;
                        break;
                    }
                }

                bool allowOf = kind is SvgCssSimpleSelectorKind.NthChild or SvgCssSimpleSelectorKind.NthLastChild;
                if ((of != close && !allowOf) || !this.TryParseAnB(argumentStart, of, out int a, out int b, out bool inRange))
                {
                    return false;
                }

                maxSpecificity = 0;
                complex = false;
                if (of != close && !this.TryParseList(of + 1, close, depth + 1, false, false, out maxSpecificity, out complex))
                {
                    return false;
                }

                // Chromium keeps a selector whose A or B lies outside [-2^30, 2^30 - 1] but never matches it.
                scratch.Add(inRange
                    ? new SvgCssSimpleSelector(kind, null, this.AddFunctional(a, b, listStart, complex))
                    : new SvgCssSimpleSelector(SvgCssSimpleSelectorKind.Never, null));
                classes++;
                AddSpecificity(maxSpecificity, ref ids, ref classes, ref types);
            }

            isComplex |= complex;
            i = close + 1;
            return true;
        }
        finally
        {
            this.scratchSelectors.Truncate(listStart);
        }
    }

    /// <summary>
    /// Adds a packed specificity to the counts of a complex selector.
    /// </summary>
    /// <param name="specificity">The packed specificity.</param>
    /// <param name="ids">The id count.</param>
    /// <param name="classes">The class count.</param>
    /// <param name="types">The type count.</param>
    private static void AddSpecificity(int specificity, ref int ids, ref int classes, ref int types)
    {
        ids += (specificity >> 16) & 0xFF;
        classes += (specificity >> 8) & 0xFF;
        types += specificity & 0xFF;
    }

    /// <summary>
    /// Copies a parsed argument list from the selector scratch stack to the argument table of the style sheet, and adds
    /// the side table entry of the functional pseudo-class that takes it.
    /// </summary>
    /// <param name="a">The step of an An+B expression, or 0.</param>
    /// <param name="b">The offset of an An+B expression, or 0.</param>
    /// <param name="listStart">The position of the first triple of the list on the scratch stack.</param>
    /// <param name="isComplex">Whether the arguments contain a combinator.</param>
    /// <returns>The index of the entry in <see cref="SvgStyleSheet.FunctionalSelectors"/>.</returns>
    private int AddFunctional(int a, int b, int listStart, bool isComplex)
    {
        ReadOnlySpan<int> list = this.scratchSelectors.AsSpan();
        List<SvgCssComplexSelector> arguments = this.sheet.Arguments;
        int start = arguments.Count;
        for (int i = listStart; i < list.Length; i += 3)
        {
            arguments.Add(new SvgCssComplexSelector(list[i], list[i + 1], list[i + 2]));
        }

        this.sheet.FunctionalSelectors.Add(new SvgCssFunctionalSelector(a, b, start, arguments.Count - start, isComplex));
        return this.sheet.FunctionalSelectors.Count - 1;
    }

    /// <summary>
    /// Parses the content of an attribute selector between its brackets.
    /// </summary>
    /// <param name="start">The first token after <c>[</c>.</param>
    /// <param name="end">The index of <c>]</c>.</param>
    /// <param name="selector">The attribute selector.</param>
    /// <returns><see langword="true"/> if the selector is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseAttribute(int start, int end, out SvgCssSimpleSelector selector)
    {
        selector = default;
        int i = start;
        _ = this.SkipWhitespace(ref i, end);
        if (i >= end)
        {
            return false;
        }

        // "[|name]" is an attribute in no namespace, the same as no prefix. "[*|name]" matches any namespace. A named
        // prefix is undeclared without @namespace rules.
        bool anyNamespace = false;
        if (this.IsDelimiter(i, '|'))
        {
            i++;
        }
        else if ((this.tokens[i].Kind == SvgCssTokenKind.Ident || this.IsDelimiter(i, '*'))
            && i + 2 < end && this.IsDelimiter(i + 1, '|') && this.tokens[i + 2].Kind == SvgCssTokenKind.Ident)
        {
            if (this.tokens[i].Kind == SvgCssTokenKind.Ident)
            {
                return false;
            }

            anyNamespace = true;
            i += 2;
        }

        if (i >= end || this.tokens[i].Kind != SvgCssTokenKind.Ident)
        {
            return false;
        }

        string name = this.names.Get(this.Name(i));
        i++;
        _ = this.SkipWhitespace(ref i, end);
        if (i == end)
        {
            selector = this.AddAttribute(name, new SvgCssAttributeSelector(SvgCssAttributeOperator.Presence, null, false, anyNamespace));
            return true;
        }

        SvgCssAttributeOperator attributeOperator;
        if (this.IsDelimiter(i, '='))
        {
            attributeOperator = SvgCssAttributeOperator.Exact;
            i++;
        }
        else
        {
            if (this.tokens[i].Kind != SvgCssTokenKind.Delim || i + 1 >= end || !this.IsDelimiter(i + 1, '='))
            {
                return false;
            }

            switch (this.source[this.tokens[i].Start])
            {
                case '~':
                    attributeOperator = SvgCssAttributeOperator.Includes;
                    break;
                case '|':
                    attributeOperator = SvgCssAttributeOperator.DashMatch;
                    break;
                case '^':
                    attributeOperator = SvgCssAttributeOperator.Prefix;
                    break;
                case '$':
                    attributeOperator = SvgCssAttributeOperator.Suffix;
                    break;
                case '*':
                    attributeOperator = SvgCssAttributeOperator.Substring;
                    break;
                default:
                    return false;
            }

            i += 2;
        }

        _ = this.SkipWhitespace(ref i, end);
        if (i >= end || this.tokens[i].Kind is not (SvgCssTokenKind.Ident or SvgCssTokenKind.String))
        {
            return false;
        }

        string value = this.tokens[i].GetValue(this.source);
        i++;
        _ = this.SkipWhitespace(ref i, end);
        bool ignoreCase = false;
        if (i < end && this.tokens[i].Kind == SvgCssTokenKind.Ident)
        {
            ReadOnlySpan<char> modifier = this.Name(i);
            if (IsKeyword(modifier, "i"))
            {
                ignoreCase = true;
            }
            else if (!IsKeyword(modifier, "s"))
            {
                return false;
            }

            i++;
            _ = this.SkipWhitespace(ref i, end);
        }

        if (i != end)
        {
            return false;
        }

        selector = this.AddAttribute(name, new SvgCssAttributeSelector(attributeOperator, value, ignoreCase, anyNamespace));
        return true;
    }

    /// <summary>
    /// Adds the side table entry of an attribute selector and records the tested name.
    /// </summary>
    /// <param name="name">The attribute local name.</param>
    /// <param name="test">The test of the selector.</param>
    /// <returns>The simple selector.</returns>
    private SvgCssSimpleSelector AddAttribute(string name, in SvgCssAttributeSelector test)
    {
        this.sheet.AddAttributeName(name);
        this.sheet.AttributeSelectors.Add(test);
        return new SvgCssSimpleSelector(SvgCssSimpleSelectorKind.Attribute, name, this.sheet.AttributeSelectors.Count - 1);
    }

    /// <summary>
    /// Parses an An+B expression (CSS Syntax Level 3, section 6).
    /// </summary>
    /// <param name="start">The first token.</param>
    /// <param name="end">The token after the last.</param>
    /// <param name="a">The step; zero when it is out of range.</param>
    /// <param name="b">The offset; zero when it is out of range.</param>
    /// <param name="inRange">Whether both values lie in [-2^30, 2^30 - 1], the range that Chromium matches.</param>
    /// <returns><see langword="true"/> if the expression is valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseAnB(int start, int end, out int a, out int b, out bool inRange)
    {
        a = 0;
        b = 0;
        inRange = false;
        long step = 0;
        long offset = 0;
        int i = start;
        _ = this.SkipWhitespace(ref i, end);
        if (i == end)
        {
            return false;
        }

        SvgCssToken first = this.tokens[i++];
        if (first.Kind == SvgCssTokenKind.Ident)
        {
            ReadOnlySpan<char> text = this.Name(i - 1);
            if (IsKeyword(text, "odd"))
            {
                step = 2;
                offset = 1;
            }
            else if (IsKeyword(text, "even"))
            {
                step = 2;
            }
            else if (!TryParseNIdentifier(text, out step, out offset, out bool needsOffset)
                || (needsOffset && !this.TryParseTrailingOffset(ref i, end, ref offset, text[^1] == '-')))
            {
                return false;
            }
        }
        else if (first.Kind == SvgCssTokenKind.Dimension)
        {
            ReadOnlySpan<char> unit = this.Name(i - 1);
            if (!TryParseNUnit(unit, out offset, out bool unitNeedsOffset)
                || !TryParseInteger(this.source[first.Start..first.ValueStart], out step)
                || ((unit.Length == 1 || unitNeedsOffset) && !this.TryParseTrailingOffset(ref i, end, ref offset, unitNeedsOffset)))
            {
                return false;
            }
        }
        else if (first.Kind == SvgCssTokenKind.Number)
        {
            if (!TryParseInteger(first.GetRawValue(this.source), out offset))
            {
                return false;
            }
        }
        else if (first.IsDelimiter(this.source, '+'))
        {
            // "+n": the identifier must follow the sign directly and must not start with another sign.
            if (i >= end || this.tokens[i].Kind != SvgCssTokenKind.Ident)
            {
                return false;
            }

            ReadOnlySpan<char> text = this.Name(i);
            i++;
            if (text.Length == 0 || text[0] == '-'
                || !TryParseNIdentifier(text, out step, out offset, out bool needsOffset)
                || (needsOffset && !this.TryParseTrailingOffset(ref i, end, ref offset, text[^1] == '-')))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        _ = this.SkipWhitespace(ref i, end);
        if (i != end)
        {
            return false;
        }

        const long limit = 1L << 30;
        inRange = step >= -limit && step < limit && offset >= -limit && offset < limit;
        if (inRange)
        {
            a = (int)step;
            b = (int)offset;
        }

        return true;
    }

    /// <summary>
    /// Parses the optional signed integer after the <c>n</c> of an An+B expression.
    /// </summary>
    /// <param name="i">The token index; advanced past the integer.</param>
    /// <param name="end">The end of the range.</param>
    /// <param name="offset">The offset, changed by the integer.</param>
    /// <param name="required">
    /// Whether the expression ended with <c>n-</c>, so an unsigned integer must follow and is subtracted.
    /// </param>
    /// <returns><see langword="true"/> if the tokens are valid; otherwise, <see langword="false"/>.</returns>
    private bool TryParseTrailingOffset(ref int i, int end, ref long offset, bool required)
    {
        _ = this.SkipWhitespace(ref i, end);
        if (i >= end)
        {
            return !required;
        }

        SvgCssToken sign = this.tokens[i++];
        long value;
        if (required)
        {
            if (sign.Kind != SvgCssTokenKind.Number || !IsUnsigned(sign.GetRawValue(this.source)) || !TryParseInteger(sign.GetRawValue(this.source), out value))
            {
                return false;
            }

            offset -= value;
            return true;
        }

        if (sign.Kind == SvgCssTokenKind.Number)
        {
            // A signed integer token such as "+1" or "-1".
            ReadOnlySpan<char> text = sign.GetRawValue(this.source);
            if (IsUnsigned(text) || !TryParseInteger(text, out value))
            {
                return false;
            }

            offset += value;
            return true;
        }

        if (!sign.IsDelimiter(this.source, '+') && !sign.IsDelimiter(this.source, '-'))
        {
            return false;
        }

        long direction = sign.IsDelimiter(this.source, '-') ? -1 : 1;
        _ = this.SkipWhitespace(ref i, end);
        if (i >= end || this.tokens[i].Kind != SvgCssTokenKind.Number)
        {
            return false;
        }

        ReadOnlySpan<char> digits = this.tokens[i].GetRawValue(this.source);
        if (!IsUnsigned(digits) || !TryParseInteger(digits, out value))
        {
            return false;
        }

        offset += direction * value;
        i++;
        return true;
    }

    /// <summary>
    /// Returns whether a number token text has no sign.
    /// </summary>
    /// <param name="text">The number text.</param>
    /// <returns><see langword="true"/> if the text starts with a digit; otherwise, <see langword="false"/>.</returns>
    private static bool IsUnsigned(ReadOnlySpan<char> text) => text.Length > 0 && char.IsAsciiDigit(text[0]);

    /// <summary>
    /// Parses an identifier of the forms <c>n</c>, <c>-n</c>, <c>n-</c>, <c>-n-</c>, <c>n-&lt;digits&gt;</c>, and
    /// <c>-n-&lt;digits&gt;</c>.
    /// </summary>
    /// <param name="text">The identifier.</param>
    /// <param name="step">The step: 1 or -1.</param>
    /// <param name="offset">The offset from the digits, or 0.</param>
    /// <param name="needsOffset">Whether an offset may or must follow as separate tokens.</param>
    /// <returns><see langword="true"/> if the identifier has one of the forms; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseNIdentifier(ReadOnlySpan<char> text, out long step, out long offset, out bool needsOffset)
    {
        step = 1;
        offset = 0;
        needsOffset = false;
        if (text.Length > 0 && text[0] == '-')
        {
            step = -1;
            text = text[1..];
        }

        if (text.Length == 0 || (text[0] | 0x20) != 'n')
        {
            return false;
        }

        if (text.Length == 1 || (text.Length == 2 && text[1] == '-'))
        {
            needsOffset = true;
            return true;
        }

        if (text[1] != '-' || !IsUnsigned(text[2..]) || !TryParseInteger(text[2..], out long digits))
        {
            return false;
        }

        offset = -digits;
        return true;
    }

    /// <summary>
    /// Parses the unit of a dimension of the forms <c>n</c>, <c>n-</c>, and <c>n-&lt;digits&gt;</c>.
    /// </summary>
    /// <param name="unit">The unit.</param>
    /// <param name="offset">The offset from the digits, or 0.</param>
    /// <param name="needsOffset">Whether the unit is <c>n-</c>, so an unsigned integer must follow.</param>
    /// <returns><see langword="true"/> if the unit has one of the forms; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseNUnit(ReadOnlySpan<char> unit, out long offset, out bool needsOffset)
    {
        offset = 0;
        needsOffset = false;
        if (unit.Length == 0 || (unit[0] | 0x20) != 'n')
        {
            return false;
        }

        if (unit.Length == 1)
        {
            return true;
        }

        if (unit[1] != '-')
        {
            return false;
        }

        if (unit.Length == 2)
        {
            needsOffset = true;
            return true;
        }

        if (!IsUnsigned(unit[2..]) || !TryParseInteger(unit[2..], out long digits))
        {
            return false;
        }

        offset = -digits;
        return true;
    }

    /// <summary>
    /// Parses an optionally signed integer of decimal digits. Values beyond the <see cref="int"/> range saturate, so the
    /// arithmetic on them cannot overflow a <see cref="long"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="value">The value.</param>
    /// <returns><see langword="true"/> if the text is an integer; otherwise, <see langword="false"/>.</returns>
    private static bool TryParseInteger(ReadOnlySpan<char> text, out long value)
    {
        value = 0;
        bool negative = false;
        if (text.Length > 0 && text[0] is '+' or '-')
        {
            negative = text[0] == '-';
            text = text[1..];
        }

        if (text.IsEmpty)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            value = Math.Min((value * 10) + (c - '0'), int.MaxValue + 1L);
        }

        if (negative)
        {
            value = -value;
        }

        return true;
    }
}
