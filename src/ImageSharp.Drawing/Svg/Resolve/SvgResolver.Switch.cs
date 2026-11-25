// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>switch</c> and the conditional processing attributes.
/// </content>
/// <remarks>
/// <para>
/// An element whose conditions fail does not render, inside or outside <c>switch</c>. A <c>switch</c> renders the first
/// of its direct children that can render and whose conditions pass, and nothing else. Conditions apply to rendering
/// only: a referenced element such as a <c>clipPath</c> or a gradient works whatever its conditions say, as in browsers.
/// </para>
/// <para>
/// The conditions follow Chromium: <c>requiredFeatures</c> is ignored (SVG 2 removed it); <c>requiredExtensions</c>
/// passes when it is empty or lists only the XHTML and MathML namespaces; <c>systemLanguage</c> passes when one of its
/// comma-separated tags matches a tag of <see cref="SvgLoadOptions.Languages"/>, and fails when it is empty; see
/// <see cref="PassesSystemLanguage(string)"/>.
/// </para>
/// </remarks>
internal sealed partial class SvgResolver
{
    /// <summary>
    /// The XHTML namespace, an extension that Chromium supports.
    /// </summary>
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    /// <summary>
    /// The MathML namespace, an extension that Chromium supports.
    /// </summary>
    private const string MathMLNamespace = "http://www.w3.org/1998/Math/MathML";

    /// <summary>
    /// Resolves a <c>switch</c> element: a group around the child that it selects.
    /// </summary>
    /// <param name="index">The element index.</param>
    /// <param name="context">The context of the element.</param>
    private void ResolveSwitch(int index, in SvgResolveContext context)
    {
        int child = this.GetSwitchChild(index);
        if (child < 0)
        {
            return;
        }

        float opacity = context.Style.Opacity;
        int group = this.BeginGroup(opacity);
        this.ResolveElement(child, in context, default, default, false);
        this.EndGroup(group, opacity);
    }

    /// <summary>
    /// Returns the child that a <c>switch</c> element renders: the first direct child that is a graphics element or a
    /// container and whose conditions pass. A selected child with <c>display: none</c> still wins and renders nothing.
    /// </summary>
    /// <param name="index">The <c>switch</c> element.</param>
    /// <returns>The child, or -1 when no child qualifies.</returns>
    private int GetSwitchChild(int index)
    {
        for (int child = this.tree.GetFirstChild(index); child >= 0; child = this.tree.GetNextSibling(child))
        {
            // Unknown elements, descriptive elements, definitions, and paint servers are not candidates.
            switch (this.tree[child].Kind)
            {
                case SvgElementKind.G:
                case SvgElementKind.A:
                case SvgElementKind.Switch:
                case SvgElementKind.Svg:
                case SvgElementKind.Use:
                case SvgElementKind.Path:
                case SvgElementKind.Rect:
                case SvgElementKind.Circle:
                case SvgElementKind.Ellipse:
                case SvgElementKind.Line:
                case SvgElementKind.Polyline:
                case SvgElementKind.Polygon:
                case SvgElementKind.Image:
                case SvgElementKind.Text:
                case SvgElementKind.ForeignObject:
                    if (!this.tree[child].HasConditionalAttributes || this.PassesConditions(child))
                    {
                        return child;
                    }

                    break;
            }
        }

        return -1;
    }

    /// <summary>
    /// Computes the object bounding box of a <c>switch</c> element: the box of the child that it renders, through the
    /// transform of the child.
    /// </summary>
    /// <param name="element">The <c>switch</c> element.</param>
    /// <param name="context">The context of the element.</param>
    /// <param name="bounds">When this method returns <see langword="true"/>, contains the box in the user space of the element.</param>
    /// <returns><see langword="true"/> if the selected child has a box; otherwise, <see langword="false"/>.</returns>
    private bool TryGetSwitchBoundingBox(int element, in SvgResolveContext context, out RectangleF bounds)
    {
        bounds = default;
        int child = this.GetSwitchChild(element);
        if (child < 0)
        {
            return false;
        }

        SvgElementKind kind = this.tree[child].Kind;
        SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(child), context.Style, in this.initialStyle, this.units, kind);
        SvgResolveContext childContext = new(in style, Matrix3x2.Identity, context.Viewport, context.Depth + 1);
        if (!this.TryGetObjectBoundingBox(child, in childContext, out RectangleF box))
        {
            return false;
        }

        bounds = this.TryGetLocalTransform(child, in style, context.Viewport, out Matrix3x2 local) ? TransformBounds(box, local) : box;
        return true;
    }

    /// <summary>
    /// Evaluates the conditional processing attributes of an element.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns><see langword="true"/> if every condition passes or the element has none; otherwise, <see langword="false"/>.</returns>
    internal bool PassesConditions(int element)
    {
        if (!this.tree.TryGetConditionalAttributes(element, out SvgConditionalAttributes conditions))
        {
            return true;
        }

        if (conditions.RequiredExtensions is string extensions && !PassesRequiredExtensions(extensions))
        {
            return false;
        }

        return conditions.SystemLanguage is not string languages || this.PassesSystemLanguage(languages);
    }

    /// <summary>
    /// Evaluates <c>requiredExtensions</c>: every listed URL must name an extension that the renderer supports. Like
    /// Chromium, an empty list passes and only the XHTML and MathML namespaces count as supported.
    /// </summary>
    /// <param name="value">The attribute value, a whitespace-separated list of URLs.</param>
    /// <returns><see langword="true"/> if the condition passes; otherwise, <see langword="false"/>.</returns>
    private static bool PassesRequiredExtensions(string value)
    {
        foreach (Range range in value.AsSpan().SplitAny(" \t\r\n\f"))
        {
            ReadOnlySpan<char> extension = value.AsSpan(range);
            if (!extension.IsEmpty && !extension.SequenceEqual(XhtmlNamespace) && !extension.SequenceEqual(MathMLNamespace))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Evaluates <c>systemLanguage</c>: one listed tag must match one of <see cref="Languages"/>, ignoring ASCII case. An
    /// empty list fails.
    /// </summary>
    /// <remarks>
    /// Two tags match when they are equal, or when one is a prefix of the other followed by <c>-</c>. One direction is the
    /// SVG 1.1 rule, where the language of the user, <c>en</c>, matches the listed <c>en-GB</c>; the other is the
    /// documented rule of <see cref="SvgLoadOptions.Languages"/>, where the listed <c>en</c> matches the language
    /// <c>en-US</c>. Chromium matches in both cases.
    /// </remarks>
    /// <param name="value">The attribute value, a comma-separated list of language tags.</param>
    /// <returns><see langword="true"/> if the condition passes; otherwise, <see langword="false"/>.</returns>
    private bool PassesSystemLanguage(string value)
    {
        foreach (Range range in value.AsSpan().Split(','))
        {
            ReadOnlySpan<char> tag = SvgNumberParser.Trim(value.AsSpan(range));
            if (tag.IsEmpty)
            {
                continue;
            }

            foreach (string language in this.Languages)
            {
                if (IsLanguagePrefix(tag, language) || IsLanguagePrefix(language, tag))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether a language tag equals another, or is a prefix of it followed by <c>-</c>, ignoring ASCII case.
    /// </summary>
    /// <param name="prefix">The shorter tag.</param>
    /// <param name="tag">The longer tag.</param>
    /// <returns><see langword="true"/> if <paramref name="prefix"/> is a prefix of <paramref name="tag"/> at a subtag boundary.</returns>
    private static bool IsLanguagePrefix(ReadOnlySpan<char> prefix, ReadOnlySpan<char> tag)
        => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (tag.Length == prefix.Length || tag[prefix.Length] == '-');
}
