// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Buffers;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Matches the rules of a completed <see cref="SvgStyleSheet"/> against the elements of a document.
/// </summary>
/// <remarks>
/// <para>
/// Matching runs right to left over the flat element tree, which has parent indexes and subtree ends. The predicate
/// semantics of attribute selectors, the An+B test, and the position pseudo-classes follow the selector matcher of the
/// Jint project (<c>Jint.HtmlParser/Css/Selectors/SelectorMatcher.cs</c>, BSD 2-Clause License, see
/// THIRD-PARTY-NOTICES); the traversal is written for the flat tree.
/// </para>
/// <para>
/// Cost bounds. The combinator walk uses the result kinds of the Servo matcher: a failed compound reports whether the
/// nearest descendant combinator, the nearest subsequent-sibling combinator, or nothing may still find a match. A failed
/// descendant search that reached the root fails the whole selector, so one complex selector with only descendant and
/// child combinators costs O(compounds x depth) per element, as in phase 1. The rest is bounded with caches, built on
/// first use only, so documents with phase 1 selectors pay nothing:
/// </para>
/// <list type="bullet">
/// <item><description>The subsequent-sibling scan is memoized per (compound, element) in a 2-bit table, so each sibling is
/// tested at most once per compound, instead of once per later sibling. Without it, <c>a ~ b</c> over n siblings costs
/// O(n²).</description></item>
/// <item><description>The result of a functional pseudo-class whose arguments have combinators is memoized per
/// (selector, element), so nested <c>:is(... :not(a b) ...)</c> costs one evaluation per element and level instead of a
/// product of depths.</description></item>
/// <item><description>Sibling positions and counts are computed once for all elements in O(n); the <c>of S</c> positions
/// once per selector and parent.</description></item>
/// </list>
/// <para>
/// Together: matching a document costs O(Σ candidate rules x compounds x depth) for the descendant walk, as in phase 1,
/// plus O(n) per memoized compound or selector. The memo tables take n / 4 bytes each, and the position tables 20n bytes
/// once. Recursion depth is bounded by <see cref="SvgStyleSheet.MaxCompoundsPerSelector"/> times
/// <see cref="SvgStyleSheet.MaxSelectorNesting"/>.
/// </para>
/// </remarks>
internal sealed class SvgCssMatcher
{
    private readonly SvgStyleSheet sheet;
    private readonly List<SvgElement> elements;
    private readonly List<string> classNames;
    private readonly SvgCssAttributeValues? attributes;
    private readonly bool[]? content;
    private Dictionary<int, byte[]>? memo;
    private Dictionary<int, (int[] Index, int[] Count)>? filtered;
    private int[]? previousSibling;
    private int[]? childIndex;
    private int[]? childCount;
    private int[]? typeIndex;
    private int[]? typeCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssMatcher"/> class.
    /// </summary>
    /// <param name="sheet">The completed style sheet.</param>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="classNames">The class table that the elements index into.</param>
    /// <param name="attributes">The raw attribute values that attribute selectors test, or <see langword="null"/> when no selector tests one.</param>
    /// <param name="content">
    /// For each element, whether it has a text child, whitespace included; or <see langword="null"/> when no selector
    /// uses <c>:empty</c>.
    /// </param>
    public SvgCssMatcher(SvgStyleSheet sheet, List<SvgElement> elements, List<string> classNames, SvgCssAttributeValues? attributes, bool[]? content)
    {
        this.sheet = sheet;
        this.elements = elements;
        this.classNames = classNames;
        this.attributes = attributes;
        this.content = content;
    }

    /// <summary>
    /// The result of matching part of a selector, as in the Servo matcher.
    /// </summary>
    private enum MatchResult : byte
    {
        /// <summary>
        /// The selector matches.
        /// </summary>
        Matched,

        /// <summary>
        /// This placement fails; the nearest subsequent-sibling or descendant combinator may try another element.
        /// </summary>
        RestartFromClosestLaterSibling,

        /// <summary>
        /// This placement fails; only the nearest descendant combinator may try a higher ancestor.
        /// </summary>
        RestartFromClosestDescendant,

        /// <summary>
        /// No placement can match, so the whole selector fails for this element.
        /// </summary>
        NotMatchedGlobally
    }

    /// <summary>
    /// Collects the ranks of the rules that match an element, in ascending cascade precedence.
    /// </summary>
    /// <param name="element">The index of the element to match.</param>
    /// <param name="matches">
    /// An empty builder that receives the matching rule ranks. Only the first values, as many as the return value, are the
    /// result; the builder may hold more values after them.
    /// </param>
    /// <returns>The number of matching rules.</returns>
    public int CollectMatches(int element, ref ValueListBuilder<int> matches)
    {
        ReadOnlySpan<SvgElement> elements = CollectionsMarshal.AsSpan(this.elements);
        ref readonly SvgElement e = ref elements[element];
        if (e.Id is not null && this.sheet.TryGetIdRules(e.Id, out int[] idRules))
        {
            this.AddMatches(idRules, elements, element, ref matches);
        }

        if (e.ClassCount > 0 && this.sheet.HasClassRules)
        {
            foreach (string className in CollectionsMarshal.AsSpan(this.classNames).Slice(e.ClassStart, e.ClassCount))
            {
                if (this.sheet.TryGetClassRules(className, out int[] classRules))
                {
                    this.AddMatches(classRules, elements, element, ref matches);
                }
            }
        }

        if (this.sheet.TryGetTypeRules(e.LocalName, out int[] typeRules))
        {
            this.AddMatches(typeRules, elements, element, ref matches);
        }

        this.AddMatches(this.sheet.UniversalRules, elements, element, ref matches);

        // The buckets are each sorted, but their union is not. A rule sits in one bucket, but an element can repeat a
        // class name, so equal ranks are removed after sorting.
        Span<int> found = matches.AsSpan();
        if (found.Length < 2)
        {
            return found.Length;
        }

        found.Sort();
        int unique = 1;
        for (int i = 1; i < found.Length; i++)
        {
            if (found[i] != found[unique - 1])
            {
                found[unique++] = found[i];
            }
        }

        return unique;
    }

    /// <summary>
    /// Returns whether a rule selector matches an element.
    /// </summary>
    /// <param name="rule">The rule rank.</param>
    /// <param name="element">The index of the element.</param>
    /// <returns><see langword="true"/> if the selector matches; otherwise, <see langword="false"/>.</returns>
    public bool Matches(int rule, int element)
    {
        SvgCssRule r = this.sheet.Rules[rule];
        return this.MatchFrom(CollectionsMarshal.AsSpan(this.elements), r.CompoundStart, r.CompoundStart + r.CompoundCount - 1, element) == MatchResult.Matched;
    }

    /// <summary>
    /// Returns whether <c>An+B</c> matches a 1-based position: whether some integer n ≥ 0 gives
    /// <c>A × n + B = position</c>.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <param name="a">The step.</param>
    /// <param name="b">The offset.</param>
    /// <returns><see langword="true"/> if the position matches; otherwise, <see langword="false"/>.</returns>
    private static bool MatchAnPlusB(int position, int a, int b)
    {
        if (a == 0)
        {
            return position == b;
        }

        // n = (position - B) / A must be a whole number and not negative. The operands fit in an int, so the long
        // arithmetic cannot overflow.
        long difference = (long)position - b;
        if (difference != 0 && Math.Sign(difference) != Math.Sign(a))
        {
            return false;
        }

        return difference % a == 0;
    }

    /// <summary>
    /// Reads a 2-bit memo entry.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="element">The element index.</param>
    /// <returns>The entry; 0 means not computed.</returns>
    private static int ReadMemo(byte[] table, int element) => (table[element >> 2] >> ((element & 3) << 1)) & 3;

    /// <summary>
    /// Writes a 2-bit memo entry.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="element">The element index.</param>
    /// <param name="value">The entry, from 1 to 3.</param>
    private static void WriteMemo(byte[] table, int element, int value)
    {
        int shift = (element & 3) << 1;
        table[element >> 2] = (byte)((table[element >> 2] & ~(3 << shift)) | (value << shift));
    }

    /// <summary>
    /// Lowers an ASCII letter; other characters are unchanged.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The lowered character.</returns>
    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c;

    /// <summary>
    /// Compares two character ranges, optionally ignoring ASCII case only, as the <c>i</c> flag of attribute selectors
    /// requires.
    /// </summary>
    /// <param name="left">The first range.</param>
    /// <param name="right">The second range.</param>
    /// <param name="ignoreCase">Whether ASCII case is ignored.</param>
    /// <returns><see langword="true"/> if the ranges are equal; otherwise, <see langword="false"/>.</returns>
    private static bool TextEquals(ReadOnlySpan<char> left, ReadOnlySpan<char> right, bool ignoreCase)
    {
        if (!ignoreCase)
        {
            return left.SequenceEqual(right);
        }

        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (AsciiLower(left[i]) != AsciiLower(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether a character is ASCII whitespace as Selectors uses it for <c>~=</c>.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><see langword="true"/> for whitespace; otherwise, <see langword="false"/>.</returns>
    private static bool IsAsciiWhitespace(char c) => c is '\t' or '\n' or '\f' or '\r' or ' ';

    /// <summary>
    /// Returns whether a whitespace-separated list contains a word.
    /// </summary>
    /// <param name="value">The list.</param>
    /// <param name="word">The word, without whitespace.</param>
    /// <param name="ignoreCase">Whether ASCII case is ignored.</param>
    /// <returns><see langword="true"/> if an item equals the word; otherwise, <see langword="false"/>.</returns>
    private static bool ContainsWord(ReadOnlySpan<char> value, ReadOnlySpan<char> word, bool ignoreCase)
    {
        int i = 0;
        while (i < value.Length)
        {
            while (i < value.Length && IsAsciiWhitespace(value[i]))
            {
                i++;
            }

            int start = i;
            while (i < value.Length && !IsAsciiWhitespace(value[i]))
            {
                i++;
            }

            if (i > start && TextEquals(value[start..i], word, ignoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether a value contains a text, with the Knuth-Morris-Pratt search, so a hostile value and needle such
    /// as <c>aaa...b</c> in <c>aaa...a</c> cost O(value + text) rather than O(value × text).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="text">The text; not empty.</param>
    /// <param name="ignoreCase">Whether ASCII case is ignored.</param>
    /// <returns><see langword="true"/> if the value contains the text; otherwise, <see langword="false"/>.</returns>
    private static bool ContainsText(ReadOnlySpan<char> value, ReadOnlySpan<char> text, bool ignoreCase)
    {
        if (text.Length > value.Length)
        {
            return false;
        }

        int[]? rented = null;
        Span<int> failure = text.Length <= 128 ? stackalloc int[128] : (rented = ArrayPool<int>.Shared.Rent(text.Length));
        try
        {
            // failure[i] is the length of the longest proper prefix of text[..(i + 1)] that is also its suffix.
            failure[0] = 0;
            int prefix = 0;
            for (int i = 1; i < text.Length; i++)
            {
                while (prefix != 0 && !CharEquals(text[i], text[prefix], ignoreCase))
                {
                    prefix = failure[prefix - 1];
                }

                if (CharEquals(text[i], text[prefix], ignoreCase))
                {
                    prefix++;
                }

                failure[i] = prefix;
            }

            int matched = 0;
            foreach (char c in value)
            {
                while (matched != 0 && !CharEquals(c, text[matched], ignoreCase))
                {
                    matched = failure[matched - 1];
                }

                if (CharEquals(c, text[matched], ignoreCase) && ++matched == text.Length)
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<int>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// Compares two characters, optionally ignoring ASCII case.
    /// </summary>
    /// <param name="left">The first character.</param>
    /// <param name="right">The second character.</param>
    /// <param name="ignoreCase">Whether ASCII case is ignored.</param>
    /// <returns><see langword="true"/> if the characters are equal; otherwise, <see langword="false"/>.</returns>
    private static bool CharEquals(char left, char right, bool ignoreCase)
        => ignoreCase ? AsciiLower(left) == AsciiLower(right) : left == right;

    /// <summary>
    /// Returns whether an attribute value satisfies an attribute selector (Selectors Level 4, section 6.1 and 6.2).
    /// </summary>
    /// <param name="value">The attribute value.</param>
    /// <param name="selector">The test of the attribute selector.</param>
    /// <returns><see langword="true"/> if the value satisfies the selector; otherwise, <see langword="false"/>.</returns>
    private static bool MatchAttributeValue(ReadOnlySpan<char> value, in SvgCssAttributeSelector selector)
    {
        ReadOnlySpan<char> expected = selector.Value;
        bool ignoreCase = selector.IgnoreCase;
        switch (selector.Operator)
        {
            case SvgCssAttributeOperator.Presence:
                return true;
            case SvgCssAttributeOperator.Exact:
                return TextEquals(value, expected, ignoreCase);
            case SvgCssAttributeOperator.Includes:
                // An empty word, or one with whitespace, can never be an item of the list.
                return !expected.IsEmpty && !expected.ContainsAny(" \t\n\f\r") && ContainsWord(value, expected, ignoreCase);
            case SvgCssAttributeOperator.DashMatch:
                return TextEquals(value, expected, ignoreCase)
                    || (value.Length > expected.Length && value[expected.Length] == '-' && TextEquals(value[..expected.Length], expected, ignoreCase));
            case SvgCssAttributeOperator.Prefix:
                return !expected.IsEmpty && value.Length >= expected.Length && TextEquals(value[..expected.Length], expected, ignoreCase);
            case SvgCssAttributeOperator.Suffix:
                return !expected.IsEmpty && value.Length >= expected.Length && TextEquals(value[^expected.Length..], expected, ignoreCase);
            default:
                return !expected.IsEmpty && ContainsText(value, expected, ignoreCase);
        }
    }

    /// <summary>
    /// Appends the rules of a bucket whose selectors match the element.
    /// </summary>
    /// <param name="candidates">The candidate rule ranks.</param>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="element">The element index.</param>
    /// <param name="matches">The builder that receives the matching ranks.</param>
    private void AddMatches(int[] candidates, ReadOnlySpan<SvgElement> elements, int element, ref ValueListBuilder<int> matches)
    {
        ReadOnlySpan<SvgCssRule> rules = this.sheet.Rules;
        foreach (int rule in candidates)
        {
            SvgCssRule r = rules[rule];
            if (this.MatchFrom(elements, r.CompoundStart, r.CompoundStart + r.CompoundCount - 1, element) == MatchResult.Matched)
            {
                matches.Append(rule);
            }
        }
    }

    /// <summary>
    /// Matches the compounds from <paramref name="compound"/> leftward against <paramref name="element"/> and the elements
    /// that the combinators relate it to.
    /// </summary>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="first">The index of the leftmost compound of the selector.</param>
    /// <param name="compound">The index of the compound to match against <paramref name="element"/>.</param>
    /// <param name="element">The element index.</param>
    /// <returns>The match result.</returns>
    private MatchResult MatchFrom(ReadOnlySpan<SvgElement> elements, int first, int compound, int element)
    {
        if (!this.MatchCompound(elements, compound, element))
        {
            return MatchResult.RestartFromClosestLaterSibling;
        }

        if (compound == first)
        {
            return MatchResult.Matched;
        }

        int parent = elements[element].Parent;
        MatchResult result;
        switch (CollectionsMarshal.AsSpan(this.sheet.Compounds)[compound].Combinator)
        {
            case SvgCssCombinator.Child:
                if (parent < 0)
                {
                    return MatchResult.NotMatchedGlobally;
                }

                // A failed parent placement can only be retried higher up, by a descendant combinator further left.
                result = this.MatchFrom(elements, first, compound - 1, parent);
                return result is MatchResult.Matched or MatchResult.NotMatchedGlobally ? result : MatchResult.RestartFromClosestDescendant;

            case SvgCssCombinator.Descendant:
                for (int ancestor = parent; ancestor >= 0; ancestor = elements[ancestor].Parent)
                {
                    result = this.MatchFrom(elements, first, compound - 1, ancestor);
                    if (result is MatchResult.Matched or MatchResult.NotMatchedGlobally)
                    {
                        return result;
                    }
                }

                // No ancestor matched the left part. A higher placement of this compound has fewer ancestors, so it cannot
                // match either.
                return MatchResult.NotMatchedGlobally;

            case SvgCssCombinator.NextSibling:
                this.EnsureSiblingIndex(elements);
                int previous = this.previousSibling![element];
                return previous < 0 ? MatchResult.RestartFromClosestDescendant : this.MatchFrom(elements, first, compound - 1, previous);

            default:
                return this.MatchSubsequentSibling(elements, first, compound, element);
        }
    }

    /// <summary>
    /// Matches the compounds left of a subsequent-sibling combinator against the earlier siblings of an element.
    /// </summary>
    /// <remarks>
    /// The result S(k, x) for compound k and element x is the first result other than
    /// <see cref="MatchResult.RestartFromClosestLaterSibling"/> of matching compound k - 1 against the earlier siblings
    /// of x, nearest first, or <see cref="MatchResult.RestartFromClosestDescendant"/> when there is none. When the
    /// previous sibling y gives <see cref="MatchResult.RestartFromClosestLaterSibling"/>, S(k, x) equals S(k, y). The
    /// walk follows that chain until it finds a memoized entry or a decisive result, then stores the result for every
    /// element on the chain, so each sibling is matched at most once per compound.
    /// </remarks>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="first">The index of the leftmost compound of the selector.</param>
    /// <param name="compound">The index of the compound right of the combinator, already matched.</param>
    /// <param name="element">The element index.</param>
    /// <returns>The match result.</returns>
    private MatchResult MatchSubsequentSibling(ReadOnlySpan<SvgElement> elements, int first, int compound, int element)
    {
        this.EnsureSiblingIndex(elements);
        byte[] table = this.GetMemo(compound, elements.Length);
        ValueListBuilder<int> chain = new(stackalloc int[16]);
        try
        {
            MatchResult result;
            int current = element;
            while (true)
            {
                int known = ReadMemo(table, current);
                if (known != 0)
                {
                    result = known == 1 ? MatchResult.Matched : known == 2 ? MatchResult.RestartFromClosestDescendant : MatchResult.NotMatchedGlobally;
                    break;
                }

                chain.Append(current);
                int previous = this.previousSibling![current];
                if (previous < 0)
                {
                    result = MatchResult.RestartFromClosestDescendant;
                    break;
                }

                result = this.MatchFrom(elements, first, compound - 1, previous);
                if (result != MatchResult.RestartFromClosestLaterSibling)
                {
                    break;
                }

                current = previous;
            }

            int code = result == MatchResult.Matched ? 1 : result == MatchResult.RestartFromClosestDescendant ? 2 : 3;
            foreach (int e in chain.AsSpan())
            {
                WriteMemo(table, e, code);
            }

            return result;
        }
        finally
        {
            chain.Dispose();
        }
    }

    /// <summary>
    /// Returns whether every simple selector of a compound matches an element.
    /// </summary>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="compound">The compound index.</param>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if the compound matches; otherwise, <see langword="false"/>.</returns>
    private bool MatchCompound(ReadOnlySpan<SvgElement> elements, int compound, int element)
    {
        SvgCssCompoundSelector c = CollectionsMarshal.AsSpan(this.sheet.Compounds)[compound];
        ReadOnlySpan<SvgCssSimpleSelector> simples = CollectionsMarshal.AsSpan(this.sheet.SimpleSelectors).Slice(c.SimpleStart, c.SimpleCount);
        for (int i = 0; i < simples.Length; i++)
        {
            if (!this.MatchSimple(elements, c.SimpleStart + i, in simples[i], element))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether a simple selector matches an element.
    /// </summary>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="index">The index of the simple selector, which keys its memo table.</param>
    /// <param name="simple">The simple selector.</param>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if the selector matches; otherwise, <see langword="false"/>.</returns>
    private bool MatchSimple(ReadOnlySpan<SvgElement> elements, int index, in SvgCssSimpleSelector simple, int element)
    {
        ref readonly SvgElement e = ref elements[element];
        int parent = e.Parent;
        switch (simple.Kind)
        {
            case SvgCssSimpleSelectorKind.Type:
                return string.Equals(simple.Name, e.LocalName, StringComparison.Ordinal);
            case SvgCssSimpleSelectorKind.Universal:
                return true;
            case SvgCssSimpleSelectorKind.Id:
                return string.Equals(simple.Name, e.Id, StringComparison.Ordinal);
            case SvgCssSimpleSelectorKind.Class:
                return CollectionsMarshal.AsSpan(this.classNames).Slice(e.ClassStart, e.ClassCount).Contains(simple.Name!);
            case SvgCssSimpleSelectorKind.Attribute:
                return this.MatchAttribute(in simple, element);
            case SvgCssSimpleSelectorKind.Root:
                return parent < 0;
            case SvgCssSimpleSelectorKind.Empty:
                return e.SubtreeEnd == element + 1 && (this.content is null || !this.content[element]);

            // The root element is the only element child of the document, so it is first, last, and only.
            case SvgCssSimpleSelectorKind.FirstChild:
                return parent < 0 || element == parent + 1;
            case SvgCssSimpleSelectorKind.LastChild:
                return parent < 0 || e.SubtreeEnd == elements[parent].SubtreeEnd;
            case SvgCssSimpleSelectorKind.OnlyChild:
                return parent < 0 || (element == parent + 1 && e.SubtreeEnd == elements[parent].SubtreeEnd);
            case SvgCssSimpleSelectorKind.FirstOfType:
                this.EnsureSiblingIndex(elements);
                return this.typeIndex![element] == 1;
            case SvgCssSimpleSelectorKind.LastOfType:
                this.EnsureSiblingIndex(elements);
                return this.typeIndex![element] == this.typeCount![element];
            case SvgCssSimpleSelectorKind.OnlyOfType:
                this.EnsureSiblingIndex(elements);
                return this.typeCount![element] == 1;
            case SvgCssSimpleSelectorKind.NthChild:
            case SvgCssSimpleSelectorKind.NthLastChild:
            {
                ref readonly SvgCssFunctionalSelector nth = ref this.Functional(simple);
                bool fromEnd = simple.Kind == SvgCssSimpleSelectorKind.NthLastChild;
                int position;
                if (nth.ArgumentCount == 0)
                {
                    this.EnsureSiblingIndex(elements);
                    position = fromEnd ? this.childCount![element] - this.childIndex![element] + 1 : this.childIndex![element];
                }
                else if (!this.TryGetFilteredPosition(elements, index, in nth, element, fromEnd, out position))
                {
                    return false;
                }

                return MatchAnPlusB(position, nth.A, nth.B);
            }

            case SvgCssSimpleSelectorKind.NthOfType:
            {
                this.EnsureSiblingIndex(elements);
                ref readonly SvgCssFunctionalSelector nth = ref this.Functional(simple);
                return MatchAnPlusB(this.typeIndex![element], nth.A, nth.B);
            }

            case SvgCssSimpleSelectorKind.NthLastOfType:
            {
                this.EnsureSiblingIndex(elements);
                ref readonly SvgCssFunctionalSelector nth = ref this.Functional(simple);
                return MatchAnPlusB(this.typeCount![element] - this.typeIndex![element] + 1, nth.A, nth.B);
            }

            case SvgCssSimpleSelectorKind.Not:
                return !this.MatchArguments(elements, index, in this.Functional(simple), element);
            case SvgCssSimpleSelectorKind.Is:
            case SvgCssSimpleSelectorKind.Where:
                return this.MatchArguments(elements, index, in this.Functional(simple), element);
            default:
                return false;
        }
    }

    /// <summary>
    /// Returns whether an attribute selector matches an element.
    /// </summary>
    /// <param name="simple">The attribute selector.</param>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if the selector matches; otherwise, <see langword="false"/>.</returns>
    private bool MatchAttribute(in SvgCssSimpleSelector simple, int element)
    {
        if (this.attributes is null)
        {
            return false;
        }

        ref readonly SvgCssAttributeSelector test = ref CollectionsMarshal.AsSpan(this.sheet.AttributeSelectors)[simple.Data];
        foreach (ref readonly SvgCssAttributeValues.Entry entry in this.attributes.Get(element))
        {
            // Without a namespace prefix only attributes in no namespace match; "*|" matches every namespace.
            if (!string.Equals(entry.Name, simple.Name, StringComparison.Ordinal) || (entry.HasNamespace && !test.AnyNamespace))
            {
                continue;
            }

            if (MatchAttributeValue(entry.Value, in test))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether any argument selector of a functional pseudo-class matches an element. Arguments with combinators
    /// are memoized per element.
    /// </summary>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="index">The index of the simple selector.</param>
    /// <param name="simple">The arguments of the simple selector.</param>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if an argument matches; otherwise, <see langword="false"/>.</returns>
    private bool MatchArguments(ReadOnlySpan<SvgElement> elements, int index, in SvgCssFunctionalSelector simple, int element)
    {
        byte[]? table = null;
        if (simple.IsComplex)
        {
            // Simple selector keys are negative, so they never collide with the compound keys of the sibling scan.
            table = this.GetMemo(~index, elements.Length);
            int known = ReadMemo(table, element);
            if (known != 0)
            {
                return known == 1;
            }
        }

        bool matched = false;
        ReadOnlySpan<SvgCssComplexSelector> arguments = CollectionsMarshal.AsSpan(this.sheet.Arguments).Slice(simple.ArgumentStart, simple.ArgumentCount);
        foreach (ref readonly SvgCssComplexSelector argument in arguments)
        {
            if (this.MatchFrom(elements, argument.CompoundStart, argument.CompoundStart + argument.CompoundCount - 1, element) == MatchResult.Matched)
            {
                matched = true;
                break;
            }
        }

        if (table is not null)
        {
            WriteMemo(table, element, matched ? 1 : 2);
        }

        return matched;
    }

    /// <summary>
    /// Gets the 1-based position of an element among its siblings that match the <c>of S</c> list of a
    /// <c>:nth-child()</c> or <c>:nth-last-child()</c> selector.
    /// </summary>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="index">The index of the simple selector.</param>
    /// <param name="simple">The arguments of the simple selector.</param>
    /// <param name="element">The element index.</param>
    /// <param name="fromEnd">Whether to count from the last sibling.</param>
    /// <param name="position">The position.</param>
    /// <returns><see langword="true"/> if the element matches S; otherwise, <see langword="false"/>.</returns>
    private bool TryGetFilteredPosition(
        ReadOnlySpan<SvgElement> elements,
        int index,
        in SvgCssFunctionalSelector simple,
        int element,
        bool fromEnd,
        out int position)
    {
        position = 0;
        int parent = elements[element].Parent;
        if (parent < 0)
        {
            position = 1;
            return this.MatchArguments(elements, index, in simple, element);
        }

        this.filtered ??= [];
        if (!this.filtered.TryGetValue(index, out (int[] Index, int[] Count) table))
        {
            table = (new int[elements.Length], new int[elements.Length]);
            this.filtered.Add(index, table);
        }

        // Index 0 means not computed. The first query under a parent computes the positions of all its children at once,
        // so each child is matched against S once.
        if (table.Index[element] == 0)
        {
            int count = 0;
            int end = elements[parent].SubtreeEnd;
            for (int child = parent + 1; child < end; child = elements[child].SubtreeEnd)
            {
                table.Index[child] = this.MatchArguments(elements, index, in simple, child) ? ++count : -1;
            }

            for (int child = parent + 1; child < end; child = elements[child].SubtreeEnd)
            {
                table.Count[child] = count;
            }
        }

        int found = table.Index[element];
        if (found < 0)
        {
            return false;
        }

        position = fromEnd ? table.Count[element] - found + 1 : found;
        return true;
    }

    /// <summary>
    /// Gets the arguments of a functional pseudo-class.
    /// </summary>
    /// <param name="simple">The simple selector.</param>
    /// <returns>The arguments.</returns>
    private ref readonly SvgCssFunctionalSelector Functional(in SvgCssSimpleSelector simple)
        => ref CollectionsMarshal.AsSpan(this.sheet.FunctionalSelectors)[simple.Data];

    /// <summary>
    /// Gets the 2-bit memo table of a key, creating it on first use.
    /// </summary>
    /// <param name="key">The key: a compound index, or the complement of a simple selector index.</param>
    /// <param name="elementCount">The number of elements.</param>
    /// <returns>The table.</returns>
    private byte[] GetMemo(int key, int elementCount)
    {
        this.memo ??= [];
        if (!this.memo.TryGetValue(key, out byte[]? table))
        {
            table = new byte[(elementCount + 3) >> 2];
            this.memo.Add(key, table);
        }

        return table;
    }

    /// <summary>
    /// Computes, once, the previous element sibling, the position among siblings, the sibling count, and the position and
    /// count among siblings of the same type, for every element.
    /// </summary>
    /// <param name="elements">The elements of the document.</param>
    private void EnsureSiblingIndex(ReadOnlySpan<SvgElement> elements)
    {
        if (this.previousSibling is not null)
        {
            return;
        }

        int n = elements.Length;
        int[] previous = new int[n];
        int[] index = new int[n];
        int[] count = new int[n];
        int[] typeIndex = new int[n];
        int[] typeCount = new int[n];

        // The root is the only element child of the document.
        previous[0] = -1;
        index[0] = count[0] = typeIndex[0] = typeCount[0] = 1;

        // Each element is visited once as a child of its parent, so the pass is O(n). The children of a parent are found
        // by jumping over subtrees.
        Dictionary<string, int> types = new(StringComparer.Ordinal);
        for (int parent = 0; parent < n; parent++)
        {
            int end = elements[parent].SubtreeEnd;
            if (end == parent + 1)
            {
                continue;
            }

            types.Clear();
            int position = 0;
            int last = -1;
            for (int child = parent + 1; child < end; child = elements[child].SubtreeEnd)
            {
                previous[child] = last;
                last = child;
                index[child] = ++position;
                ref int ofType = ref CollectionsMarshal.GetValueRefOrAddDefault(types, elements[child].LocalName, out _);
                typeIndex[child] = ++ofType;
            }

            for (int child = parent + 1; child < end; child = elements[child].SubtreeEnd)
            {
                count[child] = position;
                typeCount[child] = types[elements[child].LocalName];
            }
        }

        this.childIndex = index;
        this.childCount = count;
        this.typeIndex = typeIndex;
        this.typeCount = typeCount;
        this.previousSibling = previous;
    }
}
