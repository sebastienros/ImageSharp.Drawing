// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Resolves the primitives of a <c>filter</c> element into an <see cref="SvgFilterGraph"/> for one filtered element.
/// </summary>
/// <remarks>
/// <para>
/// The direct children of the <c>filter</c> element that are filter primitives become the graph, in order; other children
/// are ignored. Each primitive computes its style from the style of the <c>filter</c> element, so
/// <c>color-interpolation-filters</c> inherits from the <c>filter</c> element, not from the filtered element. The create
/// method of each primitive type reads its attributes and inputs (see <see cref="SvgFilterPrimitive"/>); this builder then
/// applies the subregion rules and the color space, which are the same for every primitive.
/// </para>
/// <para>
/// Primitive subregion, from Filter Effects Level 1: <c>x</c>, <c>y</c>, <c>width</c>, and <c>height</c> each default to
/// the corresponding value of the default subregion, which is the union of the subregions of the inputs when every input
/// is the result of another primitive, and the filter region when an input is a standard input, when the primitive has no
/// input, and for <c>feTile</c>. The lengths follow <c>primitiveUnits</c>: user lengths with percentages of the viewport,
/// or fractions of the bounding box. A zero or negative width or height disables the primitive: its result is
/// transparent black.
/// </para>
/// </remarks>
internal static class SvgFilterBuilder
{
    /// <summary>
    /// Builds the graph of a filter.
    /// </summary>
    /// <param name="context">The build context, set up for the filtered element.</param>
    /// <param name="filter">The index of the <c>filter</c> element.</param>
    /// <param name="filterStyle">The computed style of the <c>filter</c> element.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <param name="graph">When this method returns <see cref="SvgFilterOutcome.Graph"/>, contains the graph.</param>
    /// <returns>The outcome.</returns>
    public static SvgFilterOutcome Build(
        SvgFilterBuildContext context,
        int filter,
        in SvgComputedStyle filterStyle,
        SvgUnitBasis units,
        out SvgFilterGraph? graph)
    {
        graph = null;
        SvgElementTree tree = context.Tree;
        SvgComputedStyle initial = context.Resolver.InitialStyle;
        List<SvgFilterPrimitive> primitives = [];

        for (int child = tree.GetFirstChild(filter); child >= 0; child = tree.GetNextSibling(child))
        {
            SvgElementKind kind = tree[child].Kind;
            if (!SvgFilterTables.IsPrimitive(kind))
            {
                continue;
            }

            SvgComputedStyle style = SvgComputedStyle.Compute(in tree.GetStyle(child), in filterStyle, in initial, units, kind);
            context.MoveTo(child, in style, primitives.Count);
            SvgFilterBuildResult result = Create(context, kind, out SvgFilterPrimitive? primitive);
            switch (result)
            {
                case SvgFilterBuildResult.Unsupported:
                    return SvgFilterOutcome.Unsupported;
                case SvgFilterBuildResult.Error:
                    return SvgFilterOutcome.NotRendered;
                case SvgFilterBuildResult.PassThrough:
                    primitive = new SvgFePassThrough(context.GetInput(context.Attributes.In));
                    break;
                case SvgFilterBuildResult.Transparent:
                    primitive = new SvgFeTransparent();
                    break;
            }

            // The subregion rules hold for the substitutes too: a pass-through result is clipped to its subregion.
            if (!TryGetSubregion(context, primitive!, primitives, style.FontSize, units, out RectangleF subregion))
            {
                primitive = new SvgFeTransparent();
                subregion = context.FilterRegion;
            }

            primitive!.Subregion = subregion;

            // auto lets the user agent choose; Chromium computes in sRGB then.
            primitive.ColorSpace = style.ColorInterpolationFilters == SvgColorInterpolation.LinearRgb
                ? SvgColorInterpolation.LinearRgb
                : SvgColorInterpolation.SRgb;
            primitives.Add(primitive);
            context.SetResult(context.Attributes.Result, primitives.Count - 1);
        }

        if (primitives.Count == 0)
        {
            // A filter without primitives makes the element draw nothing.
            return SvgFilterOutcome.NotRendered;
        }

        graph = new SvgFilterGraph(context.FilterRegion, [.. primitives]);
        return SvgFilterOutcome.Graph;
    }

    /// <summary>
    /// Creates the primitive of one element through the create method of its type.
    /// </summary>
    /// <param name="context">The build context, moved to the element.</param>
    /// <param name="kind">The element kind, a filter primitive.</param>
    /// <param name="primitive">When this method returns <see cref="SvgFilterBuildResult.Created"/>, contains the primitive.</param>
    /// <returns>The outcome.</returns>
    private static SvgFilterBuildResult Create(SvgFilterBuildContext context, SvgElementKind kind, out SvgFilterPrimitive? primitive)
        => kind switch
        {
            SvgElementKind.FeBlend => SvgFeBlend.Create(context, out primitive),
            SvgElementKind.FeColorMatrix => SvgFeColorMatrix.Create(context, out primitive),
            SvgElementKind.FeComponentTransfer => SvgFeComponentTransfer.Create(context, out primitive),
            SvgElementKind.FeComposite => SvgFeComposite.Create(context, out primitive),
            SvgElementKind.FeConvolveMatrix => SvgFeConvolveMatrix.Create(context, out primitive),
            SvgElementKind.FeDiffuseLighting => SvgFeDiffuseLighting.Create(context, out primitive),
            SvgElementKind.FeDisplacementMap => SvgFeDisplacementMap.Create(context, out primitive),
            SvgElementKind.FeDropShadow => SvgFeDropShadow.Create(context, out primitive),
            SvgElementKind.FeFlood => SvgFeFlood.Create(context, out primitive),
            SvgElementKind.FeGaussianBlur => SvgFeGaussianBlur.Create(context, out primitive),
            SvgElementKind.FeImage => SvgFeImage.Create(context, out primitive),
            SvgElementKind.FeMerge => SvgFeMerge.Create(context, out primitive),
            SvgElementKind.FeMorphology => SvgFeMorphology.Create(context, out primitive),
            SvgElementKind.FeOffset => SvgFeOffset.Create(context, out primitive),
            SvgElementKind.FeSpecularLighting => SvgFeSpecularLighting.Create(context, out primitive),
            SvgElementKind.FeTile => SvgFeTile.Create(context, out primitive),
            _ => SvgFeTurbulence.Create(context, out primitive),
        };

    /// <summary>
    /// Computes the subregion of a primitive.
    /// </summary>
    /// <param name="context">The build context, moved to the element of the primitive.</param>
    /// <param name="primitive">The primitive, with its inputs.</param>
    /// <param name="previous">The primitives before it, with their subregions.</param>
    /// <param name="fontSize">The font size of the primitive element.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <param name="subregion">When this method returns <see langword="true"/>, contains the subregion.</param>
    /// <returns><see langword="true"/> if the subregion has area; <see langword="false"/> if the primitive is disabled.</returns>
    private static bool TryGetSubregion(
        SvgFilterBuildContext context,
        SvgFilterPrimitive primitive,
        List<SvgFilterPrimitive> previous,
        float fontSize,
        SvgUnitBasis units,
        out RectangleF subregion)
    {
        RectangleF defaults = context.FilterRegion;
        ReadOnlySpan<int> inputs = primitive.Inputs;
        if (inputs.Length > 0 && !primitive.HasFilterRegionAsDefaultSubregion)
        {
            bool standard = false;
            RectangleF union = default;
            for (int k = 0; k < inputs.Length; k++)
            {
                if (inputs[k] < 0)
                {
                    standard = true;
                    break;
                }

                RectangleF input = previous[inputs[k]].Subregion;
                union = k == 0 ? input : RectangleF.Union(union, input);
            }

            if (!standard)
            {
                defaults = union;
            }
        }

        ref readonly SvgFilterPrimitiveAttributes attributes = ref context.Attributes;
        float x = defaults.X;
        float y = defaults.Y;
        float width = defaults.Width;
        float height = defaults.Height;
        if (context.PrimitiveUnits == SvgCoordinateUnits.ObjectBoundingBox)
        {
            // Fractions of the box; a percentage is a fraction of 1.
            RectangleF box = context.BoundingBox!.Value;
            x = attributes.X.IsSpecified ? box.X + (SvgUnits.ToUser(attributes.X, 1F, fontSize, units, 0F) * box.Width) : x;
            y = attributes.Y.IsSpecified ? box.Y + (SvgUnits.ToUser(attributes.Y, 1F, fontSize, units, 0F) * box.Height) : y;
            width = attributes.Width.IsSpecified ? SvgUnits.ToUser(attributes.Width, 1F, fontSize, units, 0F) * box.Width : width;
            height = attributes.Height.IsSpecified ? SvgUnits.ToUser(attributes.Height, 1F, fontSize, units, 0F) * box.Height : height;
        }
        else
        {
            SizeF viewport = context.Viewport;
            x = attributes.X.IsSpecified ? SvgUnits.ToUser(attributes.X, viewport.Width, fontSize, units, 0F) : x;
            y = attributes.Y.IsSpecified ? SvgUnits.ToUser(attributes.Y, viewport.Height, fontSize, units, 0F) : y;
            width = attributes.Width.IsSpecified ? SvgUnits.ToUser(attributes.Width, viewport.Width, fontSize, units, 0F) : width;
            height = attributes.Height.IsSpecified ? SvgUnits.ToUser(attributes.Height, viewport.Height, fontSize, units, 0F) : height;
        }

        subregion = new RectangleF(x, y, width, height);
        return width > 0F && height > 0F && float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(width) && float.IsFinite(height);
    }
}
