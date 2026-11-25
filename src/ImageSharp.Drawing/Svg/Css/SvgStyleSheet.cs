// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The rules of the user style sheet and of every <c>style</c> element of a document, in flat tables, with an index for
/// matching.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SvgCssParser"/> appends rules in document order. <see cref="Complete(SizeF)"/> drops the rules whose media
/// condition is false, then sorts the rest by origin (user rules first), specificity, and order of appearance, so a rule
/// index is its cascade rank. It then indexes each rule by its rightmost compound selector: by id when the compound has
/// one, else by its first class, else by type, else as universal. Matching an element then tests only the rules in the
/// buckets of its id, classes, and type, and the universal bucket, not every rule. A rule whose rightmost compound can
/// never match, such as one with a pseudo-element, is in no bucket.
/// </para>
/// <para>
/// <see cref="SvgCssMatcher"/> matches the rules against the element tree.
/// </para>
/// </remarks>
internal sealed class SvgStyleSheet
{
    /// <summary>
    /// The maximum number of compound selectors in one complex selector. Matching recurses once per compound, so longer
    /// selectors are dropped to bound the stack.
    /// </summary>
    public const int MaxCompoundsPerSelector = 64;

    /// <summary>
    /// The maximum nesting depth of functional pseudo-classes such as <c>:not(:is(...))</c>. Parsing and matching recurse
    /// once per level, so deeper selectors are invalid, which bounds the stack.
    /// </summary>
    public const int MaxSelectorNesting = 8;

    /// <summary>
    /// The maximum nesting depth of <c>@media</c> rules. Deeper rules are skipped.
    /// </summary>
    public const int MaxMediaNesting = 8;

    private static readonly int[] NoRules = [];

    private readonly List<SvgCssRule> rules = [];
    private List<string>? mediaQueries;
    private List<int>? mediaParents;
    private List<SvgCssComplexSelector>? arguments;
    private List<SvgCssAttributeSelector>? attributeSelectors;
    private List<SvgCssFunctionalSelector>? functionalSelectors;
    private HashSet<string>? attributeNames;
    private List<SvgCssSimpleSelector>? scratchSimples;
    private Dictionary<string, int[]>? rulesById;
    private Dictionary<string, int[]>? rulesByClass;
    private Dictionary<string, int[]>? rulesByType;
    private int[] universalRules = NoRules;
    private int order;

    /// <summary>
    /// Gets the simple selector table.
    /// </summary>
    public List<SvgCssSimpleSelector> SimpleSelectors { get; } = [];

    /// <summary>
    /// Gets the compound selector table.
    /// </summary>
    public List<SvgCssCompoundSelector> Compounds { get; } = [];

    /// <summary>
    /// Gets the argument selector table of the functional pseudo-classes.
    /// </summary>
    public List<SvgCssComplexSelector> Arguments => this.arguments ??= [];

    /// <summary>
    /// Gets the side table of attribute selectors, which <see cref="SvgCssSimpleSelector.Data"/> indexes.
    /// </summary>
    public List<SvgCssAttributeSelector> AttributeSelectors => this.attributeSelectors ??= [];

    /// <summary>
    /// Gets the side table of functional pseudo-classes, which <see cref="SvgCssSimpleSelector.Data"/> indexes.
    /// </summary>
    public List<SvgCssFunctionalSelector> FunctionalSelectors => this.functionalSelectors ??= [];

    /// <summary>
    /// Gets the declaration blocks that the rules point to.
    /// </summary>
    public List<SvgSpecifiedStyle> Styles { get; } = [];

    /// <summary>
    /// Gets the number of rules.
    /// </summary>
    public int Count => this.rules.Count;

    /// <summary>
    /// Gets the number of user rules. After <see cref="Complete(SizeF)"/> they are the first rules.
    /// </summary>
    public int UserRuleCount { get; private set; }

    /// <summary>
    /// Gets the rules. After <see cref="Complete(SizeF)"/> they are in ascending cascade precedence.
    /// </summary>
    public ReadOnlySpan<SvgCssRule> Rules => CollectionsMarshal.AsSpan(this.rules);

    /// <summary>
    /// Gets the local names that attribute selectors test, or <see langword="null"/> when no selector tests one. The
    /// reader keeps the raw values of these attributes only.
    /// </summary>
    public IReadOnlyCollection<string>? AttributeNames => this.attributeNames;

    /// <summary>
    /// Gets or sets a value indicating whether a selector uses <c>:empty</c>, which needs the text content of elements.
    /// </summary>
    public bool UsesEmpty { get; set; }

    /// <summary>
    /// Gets a value indicating whether a rule depends on a media condition.
    /// </summary>
    public bool HasMediaQueries => this.mediaQueries is not null;

    /// <summary>
    /// Gets the scratch list that <see cref="SvgCssSelectorParser"/> builds compounds in. It is empty between parses.
    /// </summary>
    internal List<SvgCssSimpleSelector> ScratchSimples => this.scratchSimples ??= [];

    /// <summary>
    /// Gets a value indicating whether any rule is indexed by a class name.
    /// </summary>
    public bool HasClassRules => this.rulesByClass!.Count > 0;

    /// <summary>
    /// Gets the rules whose rightmost compound has no id, class, or type.
    /// </summary>
    public int[] UniversalRules => this.universalRules;

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
    /// Records the local name of an attribute that a selector tests.
    /// </summary>
    /// <param name="name">The name.</param>
    public void AddAttributeName(string name) => (this.attributeNames ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);

    /// <summary>
    /// Adds a media condition: the media query list of a <c>media</c> attribute or an <c>@media</c> rule.
    /// </summary>
    /// <param name="query">The media query list text.</param>
    /// <param name="parent">The condition of the enclosing <c>@media</c> rule or style sheet, or -1.</param>
    /// <returns>The index of the condition.</returns>
    public int AddMedia(string query, int parent)
    {
        (this.mediaQueries ??= []).Add(query);
        (this.mediaParents ??= []).Add(parent);
        return this.mediaQueries.Count - 1;
    }

    /// <summary>
    /// Sorts the rules into cascade order and builds the match index, with media conditions evaluated against the default
    /// viewport of 300 by 150 pixels.
    /// </summary>
    public void Complete() => this.Complete(new SizeF(300F, 150F));

    /// <summary>
    /// Drops the rules whose media condition is false, sorts the rest into cascade order, and builds the match index. Call
    /// once, after the last style sheet is parsed.
    /// </summary>
    /// <param name="viewport">The size in CSS pixels that the width and height media features test.</param>
    public void Complete(SizeF viewport)
    {
        if (this.mediaQueries is not null)
        {
            // A condition holds when its own query list and every enclosing one hold. Parents precede their children, so
            // one forward pass resolves the chain.
            bool[] holds = new bool[this.mediaQueries.Count];
            for (int i = 0; i < holds.Length; i++)
            {
                int parent = this.mediaParents![i];
                holds[i] = (parent < 0 || holds[parent]) && SvgCssMediaQuery.Evaluate(this.mediaQueries[i], viewport);
            }

            _ = this.rules.RemoveAll(rule => rule.Media >= 0 && !holds[rule.Media]);
        }

        // Ascending precedence: user rules before author rules, then lower specificity first, and for equal specificity
        // the earlier rule first. The cascade splits the user rules off by rank.
        this.rules.Sort(static (a, b) =>
        {
            int result = b.IsUser.CompareTo(a.IsUser);
            if (result != 0)
            {
                return result;
            }

            result = a.Specificity.CompareTo(b.Specificity);
            return result != 0 ? result : a.Order.CompareTo(b.Order);
        });

        Dictionary<string, List<int>> byId = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> byClass = new(StringComparer.Ordinal);
        Dictionary<string, List<int>> byType = new(StringComparer.Ordinal);
        List<int> universal = [];
        ReadOnlySpan<SvgCssRule> rules = this.Rules;
        int userRules = 0;
        for (int i = 0; i < rules.Length; i++)
        {
            if (rules[i].IsUser)
            {
                userRules++;
            }

            SvgCssCompoundSelector last = this.Compounds[rules[i].CompoundStart + rules[i].CompoundCount - 1];
            string? id = null;
            string? className = null;
            string? type = null;
            bool never = false;
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
                    case SvgCssSimpleSelectorKind.Never:
                        never = true;
                        break;
                }
            }

            // Ranks are added in ascending order, so every bucket is sorted.
            if (never)
            {
                continue;
            }

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

        this.UserRuleCount = userRules;
        this.rulesById = ToBuckets(byId);
        this.rulesByClass = ToBuckets(byClass);
        this.rulesByType = ToBuckets(byType);
        this.universalRules = universal.Count == 0 ? NoRules : [.. universal];
    }

    /// <summary>
    /// Gets the rules indexed by an id.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="candidates">The rule ranks, in ascending order.</param>
    /// <returns><see langword="true"/> if rules are indexed by the id; otherwise, <see langword="false"/>.</returns>
    public bool TryGetIdRules(string id, out int[] candidates) => this.rulesById!.TryGetValue(id, out candidates!);

    /// <summary>
    /// Gets the rules indexed by a class name.
    /// </summary>
    /// <param name="className">The class name.</param>
    /// <param name="candidates">The rule ranks, in ascending order.</param>
    /// <returns><see langword="true"/> if rules are indexed by the class; otherwise, <see langword="false"/>.</returns>
    public bool TryGetClassRules(string className, out int[] candidates) => this.rulesByClass!.TryGetValue(className, out candidates!);

    /// <summary>
    /// Gets the rules indexed by an element type.
    /// </summary>
    /// <param name="type">The local name.</param>
    /// <param name="candidates">The rule ranks, in ascending order.</param>
    /// <returns><see langword="true"/> if rules are indexed by the type; otherwise, <see langword="false"/>.</returns>
    public bool TryGetTypeRules(string type, out int[] candidates) => this.rulesByType!.TryGetValue(type, out candidates!);

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
}
