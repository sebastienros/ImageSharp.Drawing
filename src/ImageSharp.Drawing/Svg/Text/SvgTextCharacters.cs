// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 1 of the text layout pipeline: flattens the subtree of one <c>text</c> element into one character sequence after
/// white space processing, with the span and the position list entries of every character.
/// </summary>
/// <remarks>
/// <para>
/// Text content children are <c>tspan</c>, <c>a</c>, <c>textPath</c>, and <c>tref</c>. Other children, children whose
/// conditional processing attributes fail, and children with <c>display: none</c> add nothing: their characters are not
/// addressable, as in Chromium. <see cref="AddTrefCharacters"/> adds the characters of <c>tref</c>
/// (<c>SvgTextCharacters.Tref.cs</c>).
/// </para>
/// <para>
/// White space follows CSS Text Level 3 on one line, as Chromium does for SVG text. Each character uses the
/// <c>white-space</c> value of its element: under <c>pre</c>, <c>pre-wrap</c>, and <c>break-spaces</c> every space,
/// tab, and newline becomes one space; otherwise tabs and newlines become spaces, a space that follows a collapsible
/// space or starts the text is removed, and a collapsible space that ends the text is removed. Chromium maps
/// <c>xml:space</c> to <c>white-space</c>, so SVG 1.1 white space handling follows from the same rules, except that a
/// newline becomes a space where SVG 1.1 removed it.
/// </para>
/// <para>
/// Position lists address characters by code point, over the addressable characters of the element's subtree in order.
/// The lists of a descendant overwrite those of its ancestors for the characters they cover; <c>rotate</c> repeats its
/// last value for the remaining characters of its element. Lengths convert to user units here, with the font size of
/// the element that has the list.
/// </para>
/// </remarks>
internal sealed partial class SvgTextCharacters
{
    private readonly SvgResolver resolver;
    private readonly SvgElementTree tree;
    private readonly int textElement;
    private readonly StringBuilder text = new();
    private readonly List<SvgTextCharacter> characters = [];
    private readonly List<SvgTextSpan> spans = [];
    private readonly List<int> addressable = [];
    private int nodeCursor;
    private bool lastCollapsible;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgTextCharacters"/> class.
    /// </summary>
    /// <param name="resolver">The resolver that computes the styles of the children.</param>
    /// <param name="textElement">The index of the <c>text</c> element.</param>
    private SvgTextCharacters(SvgResolver resolver, int textElement)
    {
        this.resolver = resolver;
        this.tree = resolver.Tree;
        this.textElement = textElement;
    }

    /// <summary>
    /// Builds the characters of a <c>text</c> element.
    /// </summary>
    /// <param name="resolver">The resolver that computes the styles of the children.</param>
    /// <param name="textElement">The index of the <c>text</c> element.</param>
    /// <param name="style">The computed style of the <c>text</c> element.</param>
    /// <param name="viewport">The viewport that percentages in the position lists refer to.</param>
    /// <returns>The characters.</returns>
    public static SvgTextContent Build(SvgResolver resolver, int textElement, in SvgComputedStyle style, SizeF viewport)
    {
        SvgTextCharacters builder = new(resolver, textElement);
        builder.spans.Add(new SvgTextSpan(textElement, SvgElementKind.Text, -1, in style, -1));
        builder.Visit(textElement, 0, -1);
        builder.RemoveTrailingSpace();
        builder.ApplyPositionLists(viewport);
        return new SvgTextContent(builder.text.ToString(), [.. builder.characters], [.. builder.spans]);
    }

    /// <summary>
    /// Adds the characters of an element and of its text content children.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="spanIndex">The span of the element.</param>
    /// <param name="textPath">The element index of the nearest <c>textPath</c>, or -1.</param>
    private void Visit(int element, int spanIndex, int textPath)
    {
        SvgTextSpan span = this.spans[spanIndex];
        span.CharStart = this.text.Length;
        span.AddressableStart = this.addressable.Count;

        for (int child = this.tree.GetFirstChild(element); child >= 0; child = this.tree.GetNextSibling(child))
        {
            this.AddNodes(element, child, spanIndex);

            SvgElementKind kind = this.tree[child].Kind;
            bool content = kind is SvgElementKind.TSpan or SvgElementKind.A or SvgElementKind.Tref
                || (kind == SvgElementKind.TextPath && textPath < 0);

            if (content && (!this.tree[child].HasConditionalAttributes || this.resolver.PassesConditions(child)))
            {
                SvgComputedStyle style = this.resolver.ComputeChildStyle(child, span.Style);
                if (style.Display != SvgDisplay.None)
                {
                    int childPath = kind == SvgElementKind.TextPath ? child : textPath;
                    this.spans.Add(new SvgTextSpan(child, kind, spanIndex, in style, childPath));
                    int childSpan = this.spans.Count - 1;
                    if (kind == SvgElementKind.Tref)
                    {
                        this.AddReferencedText(child, childSpan);
                        this.SkipNodes(child);
                    }
                    else
                    {
                        this.Visit(child, childSpan, childPath);
                    }

                    continue;
                }
            }

            this.SkipNodes(child);
        }

        this.AddNodes(element, -1, spanIndex);
        span.CharEnd = this.text.Length;
        span.AddressableEnd = this.addressable.Count;
    }

    /// <summary>
    /// Adds the span of a <c>tref</c> element and the characters that the hook <see cref="AddTrefCharacters"/> gives it.
    /// </summary>
    /// <param name="element">The index of the <c>tref</c> element.</param>
    /// <param name="spanIndex">The span of the <c>tref</c> element.</param>
    private void AddReferencedText(int element, int spanIndex)
    {
        SvgTextSpan span = this.spans[spanIndex];
        span.CharStart = this.text.Length;
        span.AddressableStart = this.addressable.Count;
        this.AddTrefCharacters(element, spanIndex);
        span.CharEnd = this.text.Length;
        span.AddressableEnd = this.addressable.Count;
    }

    /// <summary>
    /// Adds the character data of the <c>text</c> element that a <c>tref</c> references to the span of the <c>tref</c>,
    /// through <see cref="AddCharacters(ReadOnlySpan{char}, int)"/> so that white space processing applies. Implemented in
    /// <c>SvgTextCharacters.Tref.cs</c>.
    /// </summary>
    /// <param name="element">The index of the <c>tref</c> element.</param>
    /// <param name="spanIndex">The span of the <c>tref</c> element.</param>
    partial void AddTrefCharacters(int element, int spanIndex);

    /// <summary>
    /// Adds the character data nodes of an element that come before one of its children, or all remaining nodes of the
    /// element when <paramref name="before"/> is -1.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <param name="before">The child index, or -1.</param>
    /// <param name="spanIndex">The span of the element.</param>
    private void AddNodes(int element, int before, int spanIndex)
    {
        ReadOnlySpan<SvgTextNode> nodes = this.tree.GetTextNodes(this.textElement);
        while (this.nodeCursor < nodes.Length)
        {
            ref readonly SvgTextNode node = ref nodes[this.nodeCursor];
            if (node.Parent != element || (before >= 0 && node.Position > before))
            {
                return;
            }

            this.AddCharacters(this.tree.GetCharacters(in node), spanIndex);
            this.nodeCursor++;
        }
    }

    /// <summary>
    /// Skips the character data nodes inside the subtree of a child that adds no characters.
    /// </summary>
    /// <param name="child">The child index.</param>
    private void SkipNodes(int child)
    {
        ReadOnlySpan<SvgTextNode> nodes = this.tree.GetTextNodes(this.textElement);
        int end = this.tree[child].SubtreeEnd;
        while (this.nodeCursor < nodes.Length && nodes[this.nodeCursor].Parent >= child && nodes[this.nodeCursor].Parent < end)
        {
            this.nodeCursor++;
        }
    }

    /// <summary>
    /// Adds characters of one span with white space processing.
    /// </summary>
    /// <param name="source">The characters as the XML reader delivered them.</param>
    /// <param name="spanIndex">The span of the characters.</param>
    private void AddCharacters(ReadOnlySpan<char> source, int spanIndex)
    {
        // white-space-collapse holds the cascade winner of the white-space shorthand and the longhand. SVG text is one
        // line, so preserve-breaks collapses like collapse and break-spaces preserves like preserve, as in Chromium.
        SvgWhiteSpaceCollapse mode = this.spans[spanIndex].Style.Text.WhiteSpaceCollapse;
        bool preserve = mode is SvgWhiteSpaceCollapse.Preserve or SvgWhiteSpaceCollapse.BreakSpaces;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];

            // A segment break and a tab become a space in every mode. SVG text is one line, so a preserved newline does
            // not break it, as in Chromium.
            if (c is '\n' or '\r' or '\t')
            {
                c = ' ';
            }

            if (c == ' ' && !preserve)
            {
                // A collapsible space at the start of the text or after another collapsible space is removed.
                if (this.text.Length == 0 || this.lastCollapsible)
                {
                    continue;
                }

                this.Append(c, spanIndex, true);
                this.lastCollapsible = true;
                continue;
            }

            this.Append(c, spanIndex, !char.IsLowSurrogate(c));
            this.lastCollapsible = false;
        }

        // tref elements copy the characters of another text, so one text can hold far more characters than the document.
        // A text above the limit can never be counted within it, so building it stops here, before its memory grows.
        int limit = this.resolver.MaxTextCharacters;
        if (this.text.Length > limit)
        {
            throw new InvalidImageContentException($"The SVG document exceeds the text character limit of {limit} after use references are expanded.");
        }
    }

    /// <summary>
    /// Appends one code unit.
    /// </summary>
    /// <param name="c">The code unit.</param>
    /// <param name="spanIndex">The span of the code unit.</param>
    /// <param name="addressable">Whether the code unit starts a code point.</param>
    private void Append(char c, int spanIndex, bool addressable)
    {
        if (addressable)
        {
            this.addressable.Add(this.text.Length);
        }

        _ = this.text.Append(c);
        this.characters.Add(new SvgTextCharacter
        {
            Span = spanIndex,
            Flags = addressable ? SvgTextCharacterFlags.Addressable : SvgTextCharacterFlags.None,
            X = float.NaN,
            Y = float.NaN,
            Rotate = float.NaN,
        });
    }

    /// <summary>
    /// Removes a collapsible space at the end of the text, as a line end removes it, and shortens the spans that ended
    /// after it.
    /// </summary>
    private void RemoveTrailingSpace()
    {
        if (!this.lastCollapsible)
        {
            return;
        }

        int last = this.text.Length - 1;
        _ = this.text.Remove(last, 1);
        this.characters.RemoveAt(last);
        this.addressable.RemoveAt(this.addressable.Count - 1);
        foreach (SvgTextSpan span in this.spans)
        {
            span.CharEnd = Math.Min(span.CharEnd, last);
            span.CharStart = Math.Min(span.CharStart, last);
            span.AddressableEnd = Math.Min(span.AddressableEnd, this.addressable.Count);
            span.AddressableStart = Math.Min(span.AddressableStart, this.addressable.Count);
        }
    }

    /// <summary>
    /// Assigns the values of the position lists of every <c>text</c>, <c>tspan</c>, and <c>tref</c> element to the
    /// characters, in document order, so a descendant overwrites its ancestors.
    /// </summary>
    /// <param name="viewport">The viewport that percentages refer to.</param>
    private void ApplyPositionLists(SizeF viewport)
    {
        Span<SvgTextCharacter> characters = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.characters);
        SvgUnitBasis units = this.resolver.Units;
        foreach (SvgTextSpan span in this.spans)
        {
            if (span.Kind is not (SvgElementKind.Text or SvgElementKind.TSpan or SvgElementKind.Tref))
            {
                continue;
            }

            ref readonly SvgTextAttributes attributes = ref this.tree.GetText(span.Element);
            int count = span.AddressableEnd - span.AddressableStart;
            float fontSize = span.Style.FontSize;
            SvgUnitBasis spanUnits = units.WithFont(span.Style.Text);
            ReadOnlySpan<int> addressed = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(this.addressable).Slice(span.AddressableStart, count);

            ReadOnlySpan<SvgLength> x = this.tree.GetTextLengths(attributes.X);
            for (int k = 0; k < x.Length && k < count; k++)
            {
                ref SvgTextCharacter c = ref characters[addressed[k]];
                c.X = SvgUnits.ToUser(x[k], viewport.Width, fontSize, spanUnits, 0F);
                c.Flags |= SvgTextCharacterFlags.HasX;
            }

            ReadOnlySpan<SvgLength> y = this.tree.GetTextLengths(attributes.Y);
            for (int k = 0; k < y.Length && k < count; k++)
            {
                ref SvgTextCharacter c = ref characters[addressed[k]];
                c.Y = SvgUnits.ToUser(y[k], viewport.Height, fontSize, spanUnits, 0F);
                c.Flags |= SvgTextCharacterFlags.HasY;
            }

            ReadOnlySpan<SvgLength> dx = this.tree.GetTextLengths(attributes.Dx);
            for (int k = 0; k < dx.Length && k < count; k++)
            {
                ref SvgTextCharacter c = ref characters[addressed[k]];
                c.Dx = SvgUnits.ToUser(dx[k], viewport.Width, fontSize, spanUnits, 0F);
                c.Flags |= SvgTextCharacterFlags.HasShift;
            }

            ReadOnlySpan<SvgLength> dy = this.tree.GetTextLengths(attributes.Dy);
            for (int k = 0; k < dy.Length && k < count; k++)
            {
                ref SvgTextCharacter c = ref characters[addressed[k]];
                c.Dy = SvgUnits.ToUser(dy[k], viewport.Height, fontSize, spanUnits, 0F);
                c.Flags |= SvgTextCharacterFlags.HasShift;
            }

            // The last angle repeats for the remaining characters of the element, including those of its descendants
            // that have no rotate list of their own.
            ReadOnlySpan<float> rotate = this.tree.GetRotations(attributes.Rotate);
            if (rotate.Length > 0)
            {
                for (int k = 0; k < count; k++)
                {
                    ref SvgTextCharacter c = ref characters[addressed[k]];
                    c.Rotate = rotate[Math.Min(k, rotate.Length - 1)];
                    if (k < rotate.Length)
                    {
                        c.Flags |= SvgTextCharacterFlags.HasShift;
                    }
                }
            }
        }
    }
}
