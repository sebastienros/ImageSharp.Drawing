// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The kind of a simple selector.
/// </summary>
internal enum SvgCssSimpleSelectorKind : byte
{
    /// <summary>
    /// A type selector such as <c>rect</c>. Matches the element local name, case-sensitively as XML names are.
    /// </summary>
    Type,

    /// <summary>
    /// The universal selector <c>*</c>. The pseudo-class <c>:defined</c> also uses this kind, because every element of
    /// an SVG document is defined.
    /// </summary>
    Universal,

    /// <summary>
    /// An id selector such as <c>#logo</c>.
    /// </summary>
    Id,

    /// <summary>
    /// A class selector such as <c>.st0</c>.
    /// </summary>
    Class,

    /// <summary>
    /// An attribute selector such as <c>[fill="red" i]</c>.
    /// </summary>
    Attribute,

    /// <summary>
    /// <c>:root</c>, and <c>:scope</c>, which is the root in a style sheet without a scoping root.
    /// </summary>
    Root,

    /// <summary>
    /// <c>:empty</c>: no child element and no text, whitespace included, as Chromium matches it.
    /// </summary>
    Empty,

    /// <summary>
    /// <c>:first-child</c>.
    /// </summary>
    FirstChild,

    /// <summary>
    /// <c>:last-child</c>.
    /// </summary>
    LastChild,

    /// <summary>
    /// <c>:only-child</c>.
    /// </summary>
    OnlyChild,

    /// <summary>
    /// <c>:first-of-type</c>.
    /// </summary>
    FirstOfType,

    /// <summary>
    /// <c>:last-of-type</c>.
    /// </summary>
    LastOfType,

    /// <summary>
    /// <c>:only-of-type</c>.
    /// </summary>
    OnlyOfType,

    /// <summary>
    /// <c>:nth-child(An+B [of S])</c>.
    /// </summary>
    NthChild,

    /// <summary>
    /// <c>:nth-last-child(An+B [of S])</c>.
    /// </summary>
    NthLastChild,

    /// <summary>
    /// <c>:nth-of-type(An+B)</c>.
    /// </summary>
    NthOfType,

    /// <summary>
    /// <c>:nth-last-of-type(An+B)</c>.
    /// </summary>
    NthLastOfType,

    /// <summary>
    /// <c>:not(S)</c>.
    /// </summary>
    Not,

    /// <summary>
    /// <c>:is(S)</c>.
    /// </summary>
    Is,

    /// <summary>
    /// <c>:where(S)</c>. It matches as <c>:is()</c> does and differs only in specificity, which the parser computes.
    /// </summary>
    Where,

    /// <summary>
    /// A valid selector that never matches in a static document: a pseudo-element, or a pseudo-class that needs
    /// interaction, link, or form state, such as <c>:hover</c> or <c>:visited</c>.
    /// </summary>
    Never,

    /// <summary>
    /// <c>:lang(L)</c> (Selectors Level 4): the language of the element, from <see cref="SvgElementTree.GetLanguage(int)"/>,
    /// matches one of the language ranges of the argument. <see cref="SvgCssSimpleSelector.Name"/> holds the ranges in the
    /// form that <see cref="SvgCssLanguageRanges"/> reads.
    /// </summary>
    Lang
}
