// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The rules of every <c>style</c> element of a document, in flat tables, with an index for matching.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SvgCssParser"/> appends rules in document order. <see cref="Complete"/> then sorts them by specificity and
/// order of appearance, so a rule index is its cascade rank, and indexes each rule by its rightmost compound selector: by
/// id when the compound has one, else by its first class, else by type, else as universal. Matching an element then
/// tests only the rules in the buckets of its id, classes, and type, and the universal bucket, not every rule.
/// </para>
/// <para>
/// Selector matching runs right to left with the result kinds of the Servo matcher: a failed descendant search that has
/// reached the root fails the whole selector, so matching stays linear in the tree depth for each compound instead of
/// backtracking exponentially.
/// </para>
/// </remarks>
internal sealed class SvgStyleSheet
{
    /// <summary>
    /// The maximum number of compound selectors in one complex selector. Matching recurses once per compound, so longer
    /// selectors are dropped to bound the stack.
    /// </summary>
    public const int MaxCompoundsPerSelector = 64;

    private static readonly int[] NoRules = [];

    private readonly List<SvgCssRule> rules = [];
    private Dictionary<string, int[]>? rulesById;
    private Dictionary<string, int[]>? rulesByClass;
    private Dictionary<string, int[]>? rulesByType;
    private int[] universalRules = NoRules;
    private int order;

    /// <summary>
    /// The result of matching part of a selector.
    /// </summary>
    private enum MatchResult : byte
    {
        /// <summary>
        /// The selector matches.
        /// </summary>
        Matched,

        /// <summary>
        /// This placement fails; the nearest descendant combinator may try a higher ancestor.
        /// </summary>
        RestartFromClosestDescendant,

        /// <summary>
        /// No placement can match, so the whole selector fails for this element.
        /// </summary>
        NotMatchedGlobally
    }

    /// <summary>
    /// Gets the simple selector table.
    /// </summary>
    public List<SvgCssSimpleSelector> SimpleSelectors { get; } = [];

    /// <summary>
    /// Gets the compound selector table.
    /// </summary>
    public List<SvgCssCompoundSelector> Compounds { get; } = [];

    /// <summary>
    /// Gets the declaration blocks that the rules point to.
    /// </summary>
    public List<SvgSpecifiedStyle> Styles { get; } = [];

    /// <summary>
    /// Gets the number of rules.
    /// </summary>
    public int Count => this.rules.Count;

    /// <summary>
    /// Gets the rules. After <see cref="Complete"/> they are in ascending cascade precedence.
    /// </summary>
    public ReadOnlySpan<SvgCssRule> Rules => CollectionsMarshal.AsSpan(this.rules);

    /// <summary>
    /// Returns the order value for the next rule of the style sheets and advances it.
    /// </summary>
    /// <returns>The order value.</returns>
    public int NextOrder() => this.order++;

    /// <summary>
    /// Adds a rule for one complex selector.
    /// </summary>
    /// <param name="rule">The rule.</param>
    public void Add(in SvgCssRule rule) => this.rules.Add(rule);

    /// <summary>
    /// Sorts the rules into cascade order and builds the match index. Call once, after the last style sheet is parsed.
    /// </summary>
    public void Complete()
    {
        // Ascending precedence: lower specificity first, and for equal specificity the earlier rule first.
        this.rules.Sort(static (a, b) =>
        {
            int result = a.Specificity.CompareTo(b.Specificity);
            return result != 0 ? result : a.Order.CompareTo(b.Order);
        });

        Dictionary<string, List<int>> byId = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> byClass = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> byType = new(StringComparer.Ordinal);
        List<int> universal = [];
        ReadOnlySpan<SvgCssRule> rules = this.Rules;
        for (int i = 0; i < rules.Length; i++)
        {
            SvgCssCompoundSelector last = this.Compounds[rules[i].CompoundStart + rules[i].CompoundCount - 1];
            string? id = null;
            string? className = null;
            string? type = null;
            for (int s = last.SimpleStart; s < last.SimpleStart + last.SimpleCount; s++)
            {
                SvgCssSimpleSelector simple = this.SimpleSelectors[s];
                switch (simple.Kind)
                {
                    case SvgCssSimpleSelectorKind.Id:
                        id ??= simple.Name;
                        break;
                    case SvgCssSimpleSelectorKind.Class:
                        className ??= simple.Name;
                        break;
                    case SvgCssSimpleSelectorKind.Type:
                        type = simple.Name;
                        break;
                }
            }

            // Ranks are added in ascending order, so every bucket is sorted.
            if (id is not null)
            {
                AddToBucket(byId, id, i);
            }
            else if (className is not null)
            {
                AddToBucket(byClass, className, i);
            }
            else if (type is not null)
            {
                AddToBucket(byType, type, i);
            }
            else
            {
                universal.Add(i);
            }
        }

        this.rulesById = ToBuckets(byId);
        this.rulesByClass = ToBuckets(byClass);
        this.rulesByType = ToBuckets(byType);
        this.universalRules = universal.Count == 0 ? NoRules : [.. universal];
    }

    /// <summary>
    /// Collects the ranks of the rules that match an element, in ascending cascade precedence.
    /// </summary>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="classNames">The class table that the elements index into.</param>
    /// <param name="element">The index of the element to match.</param>
    /// <param name="matches">
    /// An empty builder that receives the matching rule ranks. Only the first values, as many as the return value, are the
    /// result; the builder may hold more values after them.
    /// </param>
    /// <returns>The number of matching rules.</returns>
    public int CollectMatches(ReadOnlySpan<SvgElement> elements, ReadOnlySpan<string> classNames, int element, ref ValueListBuilder<int> matches)
    {
        ref readonly SvgElement e = ref elements[element];
        if (e.Id is not null && this.rulesById!.TryGetValue(e.Id, out int[]? idRules))
        {
            this.AddMatches(idRules, elements, classNames, element, ref matches);
        }

        if (e.ClassCount > 0 && this.rulesByClass!.Count > 0)
        {
            foreach (string className in classNames.Slice(e.ClassStart, e.ClassCount))
            {
                if (this.rulesByClass.TryGetValue(className, out int[]? classRules))
                {
                    this.AddMatches(classRules, elements, classNames, element, ref matches);
                }
            }
        }

        if (this.rulesByType!.TryGetValue(e.LocalName, out int[]? typeRules))
        {
            this.AddMatches(typeRules, elements, classNames, element, ref matches);
        }

        this.AddMatches(this.universalRules, elements, classNames, element, ref matches);

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
    /// <param name="elements">The elements of the document.</param>
    /// <param name="classNames">The class table that the elements index into.</param>
    /// <param name="element">The index of the element.</param>
    /// <returns><see langword="true"/> if the selector matches; otherwise, <see langword="false"/>.</returns>
    public bool Matches(int rule, ReadOnlySpan<SvgElement> elements, ReadOnlySpan<string> classNames, int element)
    {
        SvgCssRule r = this.rules[rule];
        return this.MatchFrom(r.CompoundStart, r.CompoundStart + r.CompoundCount - 1, elements, classNames, element) == MatchResult.Matched;
    }

    /// <summary>
    /// Adds a name bucket entry.
    /// </summary>
    /// <param name="buckets">The buckets.</param>
    /// <param name="key">The name.</param>
    /// <param name="rule">The rule rank.</param>
    private static void AddToBucket(Dictionary<string, List<int>> buckets, string key, int rule)
    {
        if (!buckets.TryGetValue(key, out List<int>? bucket))
        {
            bucket = [];
            buckets.Add(key, bucket);
        }

        bucket.Add(rule);
    }

    /// <summary>
    /// Converts growing buckets to arrays.
    /// </summary>
    /// <param name="buckets">The buckets.</param>
    /// <returns>The bucket arrays by name.</returns>
    private static Dictionary<string, int[]> ToBuckets(Dictionary<string, List<int>> buckets)
    {
        Dictionary<string, int[]> result = new(buckets.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<int>> bucket in buckets)
        {
            result.Add(bucket.Key, [.. bucket.Value]);
        }

        return result;
    }

    /// <summary>
    /// Appends the rules of a bucket whose selectors match the element.
    /// </summary>
    /// <param name="candidates">The candidate rule ranks.</param>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="classNames">The class table.</param>
    /// <param name="element">The element index.</param>
    /// <param name="matches">The builder that receives the matching ranks.</param>
    private void AddMatches(
        int[] candidates,
        ReadOnlySpan<SvgElement> elements,
        ReadOnlySpan<string> classNames,
        int element,
        ref ValueListBuilder<int> matches)
    {
        foreach (int rule in candidates)
        {
            if (this.Matches(rule, elements, classNames, element))
            {
                matches.Append(rule);
            }
        }
    }

    /// <summary>
    /// Matches the compounds from <paramref name="compound"/> leftward against <paramref name="element"/> and its
    /// ancestors.
    /// </summary>
    /// <param name="first">The index of the leftmost compound of the selector.</param>
    /// <param name="compound">The index of the compound to match against <paramref name="element"/>.</param>
    /// <param name="elements">The elements of the document.</param>
    /// <param name="classNames">The class table.</param>
    /// <param name="element">The element index.</param>
    /// <returns>The match result.</returns>
    private MatchResult MatchFrom(int first, int compound, ReadOnlySpan<SvgElement> elements, ReadOnlySpan<string> classNames, int element)
    {
        if (!this.MatchCompound(this.Compounds[compound], elements[element], classNames))
        {
            // The nearest descendant combinator to the right may still find a match at a higher ancestor.
            return MatchResult.RestartFromClosestDescendant;
        }

        if (compound == first)
        {
            return MatchResult.Matched;
        }

        int parent = elements[element].Parent;
        if (this.Compounds[compound].Combinator == SvgCssCombinator.Child)
        {
            return parent < 0
                ? MatchResult.NotMatchedGlobally
                : this.MatchFrom(first, compound - 1, elements, classNames, parent);
        }

        for (int ancestor = parent; ancestor >= 0; ancestor = elements[ancestor].Parent)
        {
            MatchResult result = this.MatchFrom(first, compound - 1, elements, classNames, ancestor);
            if (result != MatchResult.RestartFromClosestDescendant)
            {
                return result;
            }
        }

        // No ancestor matched the left part. A higher placement of this compound has fewer ancestors, so it cannot match
        // either.
        return MatchResult.NotMatchedGlobally;
    }

    /// <summary>
    /// Returns whether every simple selector of a compound matches an element.
    /// </summary>
    /// <param name="compound">The compound selector.</param>
    /// <param name="element">The element.</param>
    /// <param name="classNames">The class table.</param>
    /// <returns><see langword="true"/> if the compound matches; otherwise, <see langword="false"/>.</returns>
    private bool MatchCompound(in SvgCssCompoundSelector compound, in SvgElement element, ReadOnlySpan<string> classNames)
    {
        ReadOnlySpan<SvgCssSimpleSelector> simples = CollectionsMarshal.AsSpan(this.SimpleSelectors).Slice(compound.SimpleStart, compound.SimpleCount);
        foreach (ref readonly SvgCssSimpleSelector simple in simples)
        {
            bool matched = simple.Kind switch
            {
                SvgCssSimpleSelectorKind.Type => string.Equals(simple.Name, element.LocalName, StringComparison.Ordinal),
                SvgCssSimpleSelectorKind.Id => string.Equals(simple.Name, element.Id, StringComparison.Ordinal),
                SvgCssSimpleSelectorKind.Class => classNames.Slice(element.ClassStart, element.ClassCount).Contains(simple.Name!),
                _ => true
            };

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }
}
