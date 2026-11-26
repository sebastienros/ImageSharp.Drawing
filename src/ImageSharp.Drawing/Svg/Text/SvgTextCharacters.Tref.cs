// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 1: the characters of <c>tref</c>.
/// </summary>
/// <remarks>
/// <para>
/// SVG 2 and Chromium dropped <c>tref</c>; SVG 1.1 and resvg render it. A <c>tref</c> here adds the character data of the
/// <c>text</c> element it references, as resvg does: the character data of the <c>text</c> element and of its
/// <c>tspan</c>, <c>a</c>, and <c>textPath</c> descendants, in document order, without the elements themselves, so
/// only the style of the <c>tref</c> applies. Character data that the referenced text gets through a <c>tref</c> of its
/// own is not included, so references never nest or loop. The characters of the <c>tref</c> element itself are ignored.
/// </para>
/// <para>
/// Only a <c>text</c> element in the same document can be referenced, because the document keeps character data only
/// inside <c>text</c> elements and never reads external files. The position lists of the <c>tref</c> apply to its
/// characters as those of a <c>tspan</c> do.
/// </para>
/// </remarks>
internal sealed partial class SvgTextCharacters
{
    /// <inheritdoc cref="AddTrefCharacters"/>
    partial void AddTrefCharacters(int element, int spanIndex)
    {
        string? id = this.tree.GetTrefReference(element);

        // A reference to the text element that holds the tref would include the text in itself, so it adds nothing.
        if (id is null
            || !this.tree.TryGetElementById(id, out int target)
            || this.tree[target].Kind != SvgElementKind.Text
            || target == this.textElement)
        {
            return;
        }

        foreach (ref readonly SvgTextNode node in this.tree.GetTextNodes(target))
        {
            if (this.tree[node.Parent].Kind is SvgElementKind.Text or SvgElementKind.TSpan or SvgElementKind.A or SvgElementKind.TextPath)
            {
                this.AddCharacters(this.tree.GetCharacters(in node), spanIndex);
            }
        }
    }
}
