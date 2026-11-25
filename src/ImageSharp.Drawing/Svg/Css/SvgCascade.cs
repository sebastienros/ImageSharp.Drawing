// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Runtime.InteropServices;
using SixLabors.ImageSharp.Drawing.Helpers;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Combines the style declarations of every element into one specified style per element.
/// </summary>
/// <remarks>
/// <para>
/// The cascade runs after the whole document is read, because a <c>style</c> element can follow the elements it styles.
/// Declarations apply in ascending precedence, following the origins of CSS Cascade Level 4:
/// </para>
/// <list type="number">
/// <item><description>normal user style sheet declarations, by specificity, then order of appearance;</description></item>
/// <item><description>presentation attributes, which SVG 2 places in the author origin before every author style sheet rule;</description></item>
/// <item><description>normal author style sheet declarations, by specificity, then order of appearance;</description></item>
/// <item><description>normal <c>style</c> attribute declarations;</description></item>
/// <item><description><c>!important</c> author style sheet declarations, by specificity, then order of appearance;</description></item>
/// <item><description><c>!important</c> <c>style</c> attribute declarations;</description></item>
/// <item><description><c>!important</c> user style sheet declarations, by specificity, then order of appearance.</description></item>
/// </list>
/// <para>
/// Inheritance is not applied here. When the document has no rules, matching is skipped and the element declarations
/// merge in place.
/// </para>
/// </remarks>
internal static class SvgCascade
{
    /// <summary>
    /// Runs the cascade and sets <see cref="SvgElement.StyleIndex"/> of every element.
    /// </summary>
    /// <param name="elements">The elements; their style indexes are written.</param>
    /// <param name="sources">The declarations of each element, parallel to <paramref name="elements"/>.</param>
    /// <param name="styles">
    /// The style table that <paramref name="sources"/> index into. The cascade writes each result into a slot the element
    /// owns, or appends a slot.
    /// </param>
    /// <param name="sheet">The completed style sheet, or <see langword="null"/> when the document has none.</param>
    /// <param name="matcher">The matcher of the style sheet, or <see langword="null"/> when it has no rules.</param>
    public static void Apply(
        Span<SvgElement> elements,
        ReadOnlySpan<SvgStyleSources> sources,
        List<SvgSpecifiedStyle> styles,
        SvgStyleSheet? sheet,
        SvgCssMatcher? matcher)
    {
        ReadOnlySpan<SvgCssRule> rules = sheet is null ? default : sheet.Rules;
        ReadOnlySpan<SvgSpecifiedStyle> blocks = sheet is null ? default : CollectionsMarshal.AsSpan(sheet.Styles);
        int userRules = sheet?.UserRuleCount ?? 0;
        Span<int> matchStorage = stackalloc int[32];
        ValueListBuilder<int> matches = new(matchStorage);
        try
        {
            for (int i = 0; i < elements.Length; i++)
            {
                SvgStyleSources source = sources[i];
                int matchCount = 0;
                if (matcher is not null)
                {
                    matches.Truncate(0);
                    matchCount = matcher.CollectMatches(i, ref matches);
                }

                if (matchCount == 0)
                {
                    elements[i].StyleIndex = MergeOwn(source, styles);
                    continue;
                }

                // The presentation slot belongs to this element alone, so the result can overwrite it.
                int target = source.Presentation;
                if (target < 0)
                {
                    target = styles.Count;
                    styles.Add(default);
                }

                ref SvgSpecifiedStyle style = ref CollectionsMarshal.AsSpan(styles)[target];
                ReadOnlySpan<int> matched = matches.AsSpan()[..matchCount];

                // Matched ranks are ascending and user rules have the lowest ranks, so the user rules come first.
                int userMatches = 0;
                while (userMatches < matched.Length && matched[userMatches] < userRules)
                {
                    userMatches++;
                }

                if (userMatches > 0)
                {
                    // Normal user declarations rank below the presentation attributes, which already fill the slot, so
                    // they are merged first and the presentation attributes over them.
                    SvgSpecifiedStyle merged = default;
                    foreach (int rule in matched[..userMatches])
                    {
                        if (rules[rule].NormalStyle >= 0)
                        {
                            merged.Apply(in blocks[rules[rule].NormalStyle]);
                        }
                    }

                    merged.Apply(in style);
                    style = merged;
                }

                ReadOnlySpan<int> author = matched[userMatches..];
                foreach (int rule in author)
                {
                    if (rules[rule].NormalStyle >= 0)
                    {
                        style.Apply(in blocks[rules[rule].NormalStyle]);
                    }
                }

                if (source.Inline >= 0)
                {
                    style.Apply(in CollectionsMarshal.AsSpan(styles)[source.Inline]);
                }

                foreach (int rule in author)
                {
                    if (rules[rule].ImportantStyle >= 0)
                    {
                        style.Apply(in blocks[rules[rule].ImportantStyle]);
                    }
                }

                if (source.InlineImportant >= 0)
                {
                    style.Apply(in CollectionsMarshal.AsSpan(styles)[source.InlineImportant]);
                }

                foreach (int rule in matched[..userMatches])
                {
                    if (rules[rule].ImportantStyle >= 0)
                    {
                        style.Apply(in blocks[rules[rule].ImportantStyle]);
                    }
                }

                elements[i].StyleIndex = style.IsEmpty ? -1 : target;
            }
        }
        finally
        {
            matches.Dispose();
        }
    }

    /// <summary>
    /// Merges the declarations of an element that no rule matches, in place in the slot of lowest precedence.
    /// </summary>
    /// <param name="source">The declarations of the element.</param>
    /// <param name="styles">The style table.</param>
    /// <returns>The index of the merged style, or -1 when the element has none.</returns>
    private static int MergeOwn(SvgStyleSources source, List<SvgSpecifiedStyle> styles)
    {
        Span<SvgSpecifiedStyle> table = CollectionsMarshal.AsSpan(styles);
        int target = source.Presentation;
        if (target >= 0)
        {
            if (source.Inline >= 0)
            {
                table[target].Apply(in table[source.Inline]);
            }
        }
        else
        {
            target = source.Inline;
        }

        if (target >= 0)
        {
            if (source.InlineImportant >= 0)
            {
                table[target].Apply(in table[source.InlineImportant]);
            }

            return target;
        }

        return source.InlineImportant;
    }
}
