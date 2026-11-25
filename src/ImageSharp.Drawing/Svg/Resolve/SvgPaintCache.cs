// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using SixLabors.ImageSharp.Drawing.Processing;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Builds the brushes and pens of one document and shares equal ones. Solid brushes are shared by color, pens by value,
/// and gradient brushes by gradient element, geometry, and opacity.
/// </summary>
/// <remarks>
/// The cache lives for one resolve pass. The brushes and pens it returns are immutable, so the document shares them
/// between nodes, between <c>use</c> instances, and between concurrent renders.
/// </remarks>
internal sealed class SvgPaintCache
{
    private readonly SvgElementTree tree;
    private readonly float dpi;
    private readonly SvgComputedStyle initialStyle;
    private readonly Dictionary<Color, SolidBrush> solidBrushes = [];
    private readonly Dictionary<SvgPenKey, Pen> pens = [];
    private readonly Dictionary<SvgGradientKey, Brush?> gradientBrushes = [];
    private readonly Dictionary<int, SvgResolvedGradient> gradients = [];
    private readonly List<int> chain = [];
    private readonly HashSet<int> chainMembers = [];
    private readonly List<int> ancestors = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPaintCache"/> class.
    /// </summary>
    /// <param name="tree">The element tree.</param>
    /// <param name="dpi">The pixels per inch for absolute units.</param>
    /// <param name="initialStyle">The initial computed style.</param>
    public SvgPaintCache(SvgElementTree tree, float dpi, in SvgComputedStyle initialStyle)
    {
        this.tree = tree;
        this.dpi = dpi;
        this.initialStyle = initialStyle;
    }

    /// <summary>
    /// Returns the shared solid brush of a color.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <returns>The brush.</returns>
    public SolidBrush GetSolidBrush(Color color)
    {
        if (!this.solidBrushes.TryGetValue(color, out SolidBrush? brush))
        {
            brush = new SolidBrush(color);
            this.solidBrushes.Add(color, brush);
        }

        return brush;
    }

    /// <summary>
    /// Resolves a computed <c>fill</c> or <c>stroke</c> value to a brush.
    /// </summary>
    /// <param name="paint">The computed paint.</param>
    /// <param name="currentColor">The computed <c>color</c> of the painted element.</param>
    /// <param name="opacity">The opacity that multiplies the paint: the fill or stroke opacity, times any group opacity pushed down.</param>
    /// <param name="bounds">The fill bounding box of the painted element in its user space.</param>
    /// <param name="viewport">The viewport size of the painted element, for percentages in <c>userSpaceOnUse</c> gradients.</param>
    /// <param name="fontSize">The computed font size of the painted element.</param>
    /// <returns>The brush, or <see langword="null"/> when nothing is painted.</returns>
    public Brush? ResolvePaint(SvgPaint paint, Color currentColor, float opacity, RectangleF bounds, SizeF viewport, float fontSize)
    {
        SvgPaintKind kind = paint.Kind;
        Color color = paint.Color;

        if (kind == SvgPaintKind.Url)
        {
            // A reference that names a gradient paints the gradient, which can be nothing. Any other reference uses the
            // fallback, and without one the paint is none.
            if (paint.Reference is not null
                && this.tree.TryGetElementById(paint.Reference, out int element)
                && this.tree[element].Kind is SvgElementKind.LinearGradient or SvgElementKind.RadialGradient)
            {
                return this.GetGradientBrush(element, opacity, bounds, viewport, fontSize);
            }

            kind = paint.Fallback;
        }

        return kind switch
        {
            SvgPaintKind.Color => this.GetSolidBrush(MultiplyAlpha(color, opacity)),
            SvgPaintKind.CurrentColor => this.GetSolidBrush(MultiplyAlpha(currentColor, opacity)),
            _ => null
        };
    }

    /// <summary>
    /// Returns the shared pen for a set of stroke properties.
    /// </summary>
    /// <param name="brush">The stroke brush.</param>
    /// <param name="width">The stroke width in user units. It is greater than zero.</param>
    /// <param name="lineCap">The line cap.</param>
    /// <param name="lineJoin">The line join.</param>
    /// <param name="miterLimit">The miter limit.</param>
    /// <param name="pattern">The dash pattern in multiples of the width, or <see langword="null"/> for a solid stroke.</param>
    /// <param name="patternOffset">The dash offset in multiples of the width.</param>
    /// <returns>The pen.</returns>
    public Pen GetPen(Brush brush, float width, LineCap lineCap, LineJoin lineJoin, float miterLimit, float[]? pattern, float patternOffset)
    {
        SvgPenKey key = new(brush, width, lineCap, lineJoin, miterLimit, pattern, patternOffset);
        if (this.pens.TryGetValue(key, out Pen? pen))
        {
            return pen;
        }

        StrokeOptions strokeOptions = new()
        {
            LineCap = lineCap,
            LineJoin = lineJoin,
            MiterLimit = miterLimit,
        };

        if (pattern is null)
        {
            pen = new SolidPen(new PenOptions(brush, width, null) { StrokeOptions = strokeOptions });
        }
        else
        {
            pen = new PatternPen(new PenOptions(brush, width, pattern, patternOffset) { StrokeOptions = strokeOptions });
        }

        this.pens.Add(key, pen);
        return pen;
    }

    /// <summary>
    /// Returns a brush that paints like <paramref name="brush"/> with every color alpha multiplied by
    /// <paramref name="opacity"/>. The resolve stage uses it to push a group opacity down to the only shape of the group.
    /// </summary>
    /// <param name="brush">A brush that this cache built.</param>
    /// <param name="opacity">The opacity, in the range 0 to 1.</param>
    /// <returns>The brush.</returns>
    public Brush MultiplyOpacity(Brush brush, float opacity)
    {
        switch (brush)
        {
            case SolidBrush solid:
                return this.GetSolidBrush(MultiplyAlpha(solid.Color, opacity));
            case LinearGradientBrush linear:
                return new LinearGradientBrush(
                    linear.StartPoint,
                    linear.EndPoint,
                    linear.RepetitionMode,
                    linear.GradientTransform,
                    MultiplyAlpha(linear.ColorStops, opacity));
            case RadialGradientBrush radial when radial.IsTwoCircle:
                return new RadialGradientBrush(
                    radial.Center0,
                    radial.Radius0,
                    radial.Center1!.Value,
                    radial.Radius1!.Value,
                    radial.RepetitionMode,
                    radial.GradientTransform,
                    MultiplyAlpha(radial.ColorStops, opacity));
            case RadialGradientBrush radial:
                return new RadialGradientBrush(
                    radial.Center0,
                    radial.Radius0,
                    radial.RepetitionMode,
                    radial.GradientTransform,
                    MultiplyAlpha(radial.ColorStops, opacity));
            default:
                return brush;
        }
    }

    /// <summary>
    /// Returns a pen that strokes like <paramref name="pen"/> with its brush alpha multiplied by <paramref name="opacity"/>.
    /// </summary>
    /// <param name="pen">A pen that this cache built.</param>
    /// <param name="opacity">The opacity, in the range 0 to 1.</param>
    /// <returns>The pen.</returns>
    public Pen MultiplyOpacity(Pen pen, float opacity)
    {
        float[]? pattern = pen.StrokePattern.Length == 0 ? null : pen.StrokePattern.ToArray();
        return this.GetPen(
            this.MultiplyOpacity(pen.StrokeFill, opacity),
            pen.StrokeWidth,
            pen.StrokeOptions.LineCap,
            pen.StrokeOptions.LineJoin,
            (float)pen.StrokeOptions.MiterLimit,
            pattern,
            pen.StrokePatternOffset);
    }

    /// <summary>
    /// Multiplies the alpha of a color.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="opacity">The opacity, in the range 0 to 1.</param>
    /// <returns>The color with the alpha multiplied.</returns>
    private static Color MultiplyAlpha(Color color, float opacity)
        => opacity >= 1F ? color : color.WithAlpha(color.ToScaledVector4().W * opacity);

    /// <summary>
    /// Copies gradient stops with every alpha multiplied. Each brush gets its own array, because a gradient brush sorts
    /// the array it receives in place.
    /// </summary>
    /// <param name="stops">The stops.</param>
    /// <param name="opacity">The opacity, in the range 0 to 1.</param>
    /// <returns>The new stops.</returns>
    private static ColorStop[] MultiplyAlpha(ReadOnlySpan<ColorStop> stops, float opacity)
    {
        ColorStop[] result = new ColorStop[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            result[i] = new ColorStop(stops[i].Ratio, MultiplyAlpha(stops[i].Color, opacity));
        }

        return result;
    }

    /// <summary>
    /// Returns the shared brush of a gradient element for one painted geometry.
    /// </summary>
    /// <param name="element">The index of the gradient element.</param>
    /// <param name="opacity">The opacity that multiplies the stop colors.</param>
    /// <param name="bounds">The fill bounding box of the painted element.</param>
    /// <param name="viewport">The viewport size of the painted element.</param>
    /// <param name="fontSize">The computed font size of the painted element.</param>
    /// <returns>The brush, or <see langword="null"/> when the gradient paints nothing.</returns>
    private Brush? GetGradientBrush(int element, float opacity, RectangleF bounds, SizeF viewport, float fontSize)
    {
        SvgResolvedGradient gradient = this.GetGradient(element);
        bool userSpace = gradient.Attributes.Units == SvgGradientUnits.UserSpaceOnUse;
        RectangleF box = userSpace ? new RectangleF(0F, 0F, viewport.Width, viewport.Height) : bounds;

        SvgGradientKey key = new(element, box, fontSize, opacity);
        if (!this.gradientBrushes.TryGetValue(key, out Brush? brush))
        {
            brush = this.CreateGradientBrush(gradient, userSpace, box, fontSize, opacity);
            this.gradientBrushes.Add(key, brush);
        }

        return brush;
    }

    /// <summary>
    /// Builds a gradient brush, applying the edge cases of the SVG specification.
    /// </summary>
    /// <param name="gradient">The resolved gradient.</param>
    /// <param name="userSpace">Whether the gradient uses <c>userSpaceOnUse</c> units.</param>
    /// <param name="box">The viewport rectangle for user space units, or the bounding box for bounding box units.</param>
    /// <param name="fontSize">The computed font size of the painted element.</param>
    /// <param name="opacity">The opacity that multiplies the stop colors.</param>
    /// <returns>The brush, or <see langword="null"/> when the gradient paints nothing.</returns>
    private Brush? CreateGradientBrush(SvgResolvedGradient gradient, bool userSpace, RectangleF box, float fontSize, float opacity)
    {
        ColorStop[] stops = gradient.Stops;

        // No stops paint nothing, as if the paint were none. One stop paints its color.
        if (stops.Length == 0)
        {
            return null;
        }

        // In bounding box units the geometry maps the unit square to the bounding box. A box without width or height
        // has no such mapping, and the element is not painted.
        if (!userSpace && (box.Width <= 0F || box.Height <= 0F))
        {
            return null;
        }

        Color lastColor = MultiplyAlpha(stops[^1].Color, opacity);
        if (stops.Length == 1)
        {
            return this.GetSolidBrush(lastColor);
        }

        // In user space a percentage refers to the viewport; in bounding box units every value is a fraction of the
        // box, so 100% is 1.
        float width = userSpace ? box.Width : 1F;
        float height = userSpace ? box.Height : 1F;
        float diagonal = userSpace ? SvgUnits.Diagonal(box.Size) : 1F;

        SvgGradientAttributes attributes = gradient.Attributes;
        Matrix3x2 transform = attributes.HasTransform ? attributes.Transform : Matrix3x2.Identity;
        if (!userSpace)
        {
            // The gradient transform applies in the unit square, before the mapping to the bounding box.
            transform *= new Matrix3x2(box.Width, 0F, 0F, box.Height, box.X, box.Y);
        }

        GradientRepetitionMode mode = attributes.SpreadMethod switch
        {
            SvgSpreadMethod.Reflect => GradientRepetitionMode.Reflect,
            SvgSpreadMethod.Repeat => GradientRepetitionMode.Repeat,
            _ => GradientRepetitionMode.None
        };

        if (!gradient.IsRadial)
        {
            Vector2 start = new(
                this.ToUser(attributes.X1, width, fontSize, 0F),
                this.ToUser(attributes.Y1, height, fontSize, 0F));
            Vector2 end = new(
                this.ToUser(attributes.X2, width, fontSize, width),
                this.ToUser(attributes.Y2, height, fontSize, 0F));

            // Equal end points paint the color of the last stop.
            if (start == end)
            {
                return this.GetSolidBrush(lastColor);
            }

            return new LinearGradientBrush(start, end, mode, new Matrix4x4(transform), MultiplyAlpha(stops, opacity));
        }

        Vector2 center = new(
            this.ToUser(attributes.Cx, width, fontSize, width / 2F),
            this.ToUser(attributes.Cy, height, fontSize, height / 2F));
        float radius = this.ToUser(attributes.R, diagonal, fontSize, diagonal / 2F);

        // A zero radius paints the color of the last stop.
        if (radius <= 0F)
        {
            return this.GetSolidBrush(lastColor);
        }

        Vector2 focus = new(
            attributes.Fx.IsSpecified ? this.ToUser(attributes.Fx, width, fontSize, 0F) : center.X,
            attributes.Fy.IsSpecified ? this.ToUser(attributes.Fy, height, fontSize, 0F) : center.Y);
        float focalRadius = this.ToUser(attributes.Fr, diagonal, fontSize, 0F);

        if (focus == center && focalRadius == 0F)
        {
            return new RadialGradientBrush(center, radius, mode, new Matrix4x4(transform), MultiplyAlpha(stops, opacity));
        }

        // The start circle is the focal circle and the end circle is the outer circle, as in the SVG 2 definition.
        return new RadialGradientBrush(focus, focalRadius, center, radius, mode, new Matrix4x4(transform), MultiplyAlpha(stops, opacity));
    }

    /// <summary>
    /// Converts a gradient length to the units of the gradient geometry.
    /// </summary>
    /// <param name="value">The length.</param>
    /// <param name="percentBase">The size that 100% refers to.</param>
    /// <param name="fontSize">The computed font size of the painted element.</param>
    /// <param name="defaultValue">The value of an unspecified length.</param>
    /// <returns>The value.</returns>
    private float ToUser(SvgLength value, float percentBase, float fontSize, float defaultValue)
        => SvgUnits.ToUser(value, percentBase, fontSize, this.dpi, defaultValue);

    /// <summary>
    /// Returns the resolved form of a gradient element, building it on first use.
    /// </summary>
    /// <remarks>
    /// The method follows the <c>href</c> chain iteratively, so a long chain cannot exhaust the stack. It stops at an
    /// element that is not a gradient, at a reference that does not resolve, at a gradient that is already resolved, and at
    /// a reference back into the chain. SVG makes a reference cycle an error; the reference that closes the cycle is
    /// ignored. Each element then merges its own attributes over the resolved form of the element it references, from the
    /// end of the chain back to the start, so every element of the chain is resolved once.
    /// </remarks>
    /// <param name="element">The index of the gradient element.</param>
    /// <returns>The resolved gradient.</returns>
    private SvgResolvedGradient GetGradient(int element)
    {
        if (this.gradients.TryGetValue(element, out SvgResolvedGradient? resolved))
        {
            return resolved;
        }

        List<int> chain = this.chain;
        HashSet<int> members = this.chainMembers;
        chain.Clear();
        members.Clear();
        chain.Add(element);
        members.Add(element);
        SvgResolvedGradient? tail = null;

        while (true)
        {
            string? href = this.tree.GetGradient(chain[^1]).Href;
            if (href is null
                || !this.tree.TryGetElementById(href, out int next)
                || this.tree[next].Kind is not (SvgElementKind.LinearGradient or SvgElementKind.RadialGradient)
                || members.Contains(next))
            {
                break;
            }

            if (this.gradients.TryGetValue(next, out tail))
            {
                break;
            }

            chain.Add(next);
            members.Add(next);
        }

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            int index = chain[i];
            SvgGradientAttributes merged = this.tree.GetGradient(index);
            ColorStop[]? stops = this.ReadStops(index);

            if (tail is not null)
            {
                merged = Merge(merged, tail.Attributes);
                stops ??= tail.Stops;
            }

            tail = new SvgResolvedGradient(this.tree[index].Kind == SvgElementKind.RadialGradient, merged, stops ?? []);
            this.gradients.Add(index, tail);
        }

        return tail!;
    }

    /// <summary>
    /// Fills the attributes that a gradient does not specify from the resolved attributes of the gradient it references.
    /// </summary>
    /// <param name="own">The attributes of the gradient.</param>
    /// <param name="referenced">The resolved attributes of the referenced gradient.</param>
    /// <returns>The merged attributes.</returns>
    private static SvgGradientAttributes Merge(SvgGradientAttributes own, SvgGradientAttributes referenced)
    {
        SvgGradientAttributes merged = own;
        merged.X1 = own.X1.IsSpecified ? own.X1 : referenced.X1;
        merged.Y1 = own.Y1.IsSpecified ? own.Y1 : referenced.Y1;
        merged.X2 = own.X2.IsSpecified ? own.X2 : referenced.X2;
        merged.Y2 = own.Y2.IsSpecified ? own.Y2 : referenced.Y2;
        merged.Cx = own.Cx.IsSpecified ? own.Cx : referenced.Cx;
        merged.Cy = own.Cy.IsSpecified ? own.Cy : referenced.Cy;
        merged.R = own.R.IsSpecified ? own.R : referenced.R;
        merged.Fx = own.Fx.IsSpecified ? own.Fx : referenced.Fx;
        merged.Fy = own.Fy.IsSpecified ? own.Fy : referenced.Fy;
        merged.Fr = own.Fr.IsSpecified ? own.Fr : referenced.Fr;
        merged.Units = own.Units != SvgGradientUnits.Unspecified ? own.Units : referenced.Units;
        merged.SpreadMethod = own.SpreadMethod != SvgSpreadMethod.Unspecified ? own.SpreadMethod : referenced.SpreadMethod;

        if (!own.HasTransform && referenced.HasTransform)
        {
            merged.HasTransform = true;
            merged.Transform = referenced.Transform;
        }

        return merged;
    }

    /// <summary>
    /// Reads the <c>stop</c> children of a gradient element.
    /// </summary>
    /// <param name="element">The index of the gradient element.</param>
    /// <returns>The stops, or <see langword="null"/> when the element has no <c>stop</c> child.</returns>
    private ColorStop[]? ReadStops(int element)
    {
        int count = 0;
        for (int child = this.tree.GetFirstChild(element); child >= 0; child = this.tree.GetNextSibling(child))
        {
            if (this.tree[child].Kind == SvgElementKind.Stop)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return null;
        }

        // A stop takes stop-color and stop-opacity from its own style, inherit refers to the gradient element, and
        // currentColor refers to the color property that the stop inherits through its ancestors in the document.
        SvgComputedStyle gradientStyle = this.ComputeStyle(element);
        ColorStop[] stops = new ColorStop[count];
        float previous = 0F;
        int index = 0;

        for (int child = this.tree.GetFirstChild(element); child >= 0; child = this.tree.GetNextSibling(child))
        {
            if (this.tree[child].Kind != SvgElementKind.Stop)
            {
                continue;
            }

            SvgComputedStyle style = SvgComputedStyle.Compute(in this.tree.GetStyle(child), in gradientStyle, in this.initialStyle, this.dpi);
            Color color = style.StopColor.Kind == SvgPaintKind.CurrentColor ? style.Color : style.StopColor.Color;

            // Each offset is at least the offset of the stop before it.
            float offset = MathF.Max(this.tree.GetStopOffset(child), previous);
            previous = offset;
            stops[index++] = new ColorStop(offset, MultiplyAlpha(color, style.StopOpacity));
        }

        return stops;
    }

    /// <summary>
    /// Computes the style of an element through its ancestors in the document, as paint servers use it: a paint server
    /// inherits from its place in the document, not from the element it paints.
    /// </summary>
    /// <param name="element">The element index.</param>
    /// <returns>The computed style.</returns>
    private SvgComputedStyle ComputeStyle(int element)
    {
        List<int> ancestors = this.ancestors;
        ancestors.Clear();
        for (int index = element; index >= 0; index = this.tree[index].Parent)
        {
            ancestors.Add(index);
        }

        SvgComputedStyle style = this.initialStyle;
        for (int i = ancestors.Count - 1; i >= 0; i--)
        {
            style = SvgComputedStyle.Compute(in this.tree.GetStyle(ancestors[i]), in style, in this.initialStyle, this.dpi);
        }

        return style;
    }
}
