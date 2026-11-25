// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The arguments of a functional pseudo-class: <c>:nth-*()</c>, <c>:not()</c>, <c>:is()</c>, or <c>:where()</c>.
/// </summary>
internal readonly struct SvgCssFunctionalSelector
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCssFunctionalSelector"/> struct.
    /// </summary>
    /// <param name="a">The step of an An+B expression.</param>
    /// <param name="b">The offset of an An+B expression.</param>
    /// <param name="argumentStart">The index of the first argument selector.</param>
    /// <param name="argumentCount">The number of argument selectors.</param>
    /// <param name="isComplex">Whether the arguments contain a combinator, directly or nested.</param>
    public SvgCssFunctionalSelector(int a, int b, int argumentStart, int argumentCount, bool isComplex)
    {
        this.A = a;
        this.B = b;
        this.ArgumentStart = argumentStart;
        this.ArgumentCount = argumentCount;
        this.IsComplex = isComplex;
    }

    /// <summary>
    /// Gets the step A of the An+B expression of a <c>:nth-*</c> selector, in [-2^30, 2^30 - 1].
    /// </summary>
    public int A { get; }

    /// <summary>
    /// Gets the offset B of the An+B expression of a <c>:nth-*</c> selector, in [-2^30, 2^30 - 1].
    /// </summary>
    public int B { get; }

    /// <summary>
    /// Gets the index of the first argument selector in <see cref="SvgStyleSheet.Arguments"/>: the selector list of
    /// <c>:not()</c>, <c>:is()</c>, and <c>:where()</c>, or the <c>of S</c> list of <c>:nth-child()</c> and
    /// <c>:nth-last-child()</c>.
    /// </summary>
    public int ArgumentStart { get; }

    /// <summary>
    /// Gets the number of argument selectors. Zero for <c>:nth-child()</c> without <c>of S</c>, and for an empty
    /// forgiving list of <c>:is()</c> or <c>:where()</c>, which matches nothing.
    /// </summary>
    public int ArgumentCount { get; }

    /// <summary>
    /// Gets a value indicating whether an argument selector has a combinator, directly or in a nested argument. Matching
    /// such arguments walks other elements, so the matcher memoizes their result per element.
    /// </summary>
    public bool IsComplex { get; }
}
