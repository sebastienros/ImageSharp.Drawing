// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The language ranges of a <c>:lang()</c> selector (Selectors Level 4) and their matching against the language of an
/// element.
/// </summary>
/// <remarks>
/// <para>
/// The selector parser stores the ranges of one selector as one string in <see cref="SvgCssSimpleSelector.Name"/>, each
/// range as written, with escapes decoded, and followed by <see cref="Separator"/>, a character that no range can match.
/// </para>
/// <para>
/// A range matches by the extended filtering of RFC 4647, section 3.3.2, which Selectors Level 4 specifies, ignoring ASCII
/// case: the first subtag of the range must equal the first subtag of the language or be the wildcard <c>*</c>; each later
/// subtag of the range, unless it is <c>*</c>, must equal a later subtag of the language, in order, where language
/// subtags may be skipped up to a singleton such as <c>x</c>. So <c>fr</c> matches <c>fr-CA</c>, <c>de-DE</c> matches
/// <c>de-Latn-DE</c>, and <c>*-CH</c> matches <c>de-CH</c>. The empty range matches only an element whose language is
/// the empty string, which means unknown, and no other range matches such an element.
/// </para>
/// <para>
/// The Chromium oracle implements the Selectors Level 3 form only: one identifier, matched as the whole language or as a
/// prefix of it followed by <c>-</c>. It drops a list of ranges, a quoted range, and a wildcard, and does not match
/// <c>de-DE</c> against <c>de-Latn-DE</c>. This renderer follows the specification; both agree on every selector of the
/// Level 3 form.
/// </para>
/// </remarks>
internal static class SvgCssLanguageRanges
{
    /// <summary>
    /// The character that ends each range in the stored form.
    /// </summary>
    public const char Separator = '\0';

    /// <summary>
    /// Returns whether a language matches one of the ranges of a selector.
    /// </summary>
    /// <param name="language">The language of the element as written, or <see langword="null"/> when it has none.</param>
    /// <param name="ranges">The stored ranges.</param>
    /// <returns><see langword="true"/> if a range matches; otherwise, <see langword="false"/>.</returns>
    public static bool Matches(string? language, string ranges)
    {
        if (language is null)
        {
            return false;
        }

        ReadOnlySpan<char> rest = ranges;
        while (!rest.IsEmpty)
        {
            int end = rest.IndexOf(Separator);
            ReadOnlySpan<char> range = rest[..end];
            rest = rest[(end + 1)..];
            if (range.IsEmpty ? language.Length == 0 : language.Length > 0 && MatchesRange(range, language))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches one non-empty range against a non-empty language by extended filtering.
    /// </summary>
    /// <param name="range">The range in ASCII lowercase.</param>
    /// <param name="language">The language.</param>
    /// <returns><see langword="true"/> if the range matches; otherwise, <see langword="false"/>.</returns>
    private static bool MatchesRange(ReadOnlySpan<char> range, ReadOnlySpan<char> language)
    {
        ReadOnlySpan<char> rangeSubtag = NextSubtag(ref range);
        ReadOnlySpan<char> languageSubtag = NextSubtag(ref language);
        if (!rangeSubtag.SequenceEqual("*") && !rangeSubtag.Equals(languageSubtag, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        while (!range.IsEmpty)
        {
            rangeSubtag = NextSubtag(ref range);
            if (rangeSubtag.SequenceEqual("*"))
            {
                continue;
            }

            // Look for the subtag later in the language; a singleton starts an extension, which ends the search.
            while (true)
            {
                if (language.IsEmpty)
                {
                    return false;
                }

                languageSubtag = NextSubtag(ref language);
                if (rangeSubtag.Equals(languageSubtag, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (languageSubtag.Length == 1)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the next subtag of a tag and removes it and its following <c>-</c> from the tag.
    /// </summary>
    /// <param name="tag">The rest of the tag.</param>
    /// <returns>The subtag; empty for an empty subtag.</returns>
    private static ReadOnlySpan<char> NextSubtag(ref ReadOnlySpan<char> tag)
    {
        int dash = tag.IndexOf('-');
        if (dash < 0)
        {
            ReadOnlySpan<char> last = tag;
            tag = [];
            return last;
        }

        ReadOnlySpan<char> subtag = tag[..dash];
        tag = tag[(dash + 1)..];
        return subtag;
    }
}
