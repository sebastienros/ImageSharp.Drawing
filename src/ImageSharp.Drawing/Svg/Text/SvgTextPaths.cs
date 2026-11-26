// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Stage 6 of the text layout pipeline: maps the clusters of each <c>textPath</c> onto its path.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PlaceOnPaths"/> is in <c>SvgTextPaths.OnPath.cs</c>. Contract: the stage runs after text anchoring.
/// A cluster lies on a path when its span has <see cref="SvgTextSpan.TextPath"/>. Its <see cref="SvgTextCluster.X"/> is
/// its anchored position along the line, which becomes a distance along the path; its <see cref="SvgTextCluster.Y"/> a
/// distance perpendicular to it. The stage sets <see cref="SvgTextCluster.X"/>, <see cref="SvgTextCluster.Y"/>, and
/// <see cref="SvgTextCluster.Rotation"/> to the placed pen position and direction in user units of the <c>text</c>
/// element, sets <see cref="SvgTextCluster.Skipped"/> for clusters that fall off the path, and moves the clusters after a
/// <c>textPath</c> to continue at the end of the path. The resolver passed in resolves the referenced element and its
/// transform.
/// </para>
/// </remarks>
internal static partial class SvgTextPaths
{
    /// <summary>
    /// Maps the clusters on paths.
    /// </summary>
    /// <param name="layout">The anchored layout.</param>
    /// <param name="resolver">The resolver, which gives access to the element tree and the referenced geometry.</param>
    public static void Apply(SvgTextLayout layout, SvgResolver resolver)
    {
        bool placed = false;
        PlaceOnPaths(layout, resolver, ref placed);
        if (placed)
        {
            return;
        }

        SvgTextSpan[] spans = layout.Content.Spans;
        foreach (ref SvgTextCluster cluster in layout.Clusters)
        {
            if (spans[cluster.Span].TextPath >= 0)
            {
                cluster.Skipped = true;
            }
        }
    }

    /// <summary>
    /// Places the clusters on their paths, as the remarks of the type describe, and sets <paramref name="placed"/>.
    /// </summary>
    /// <param name="layout">The anchored layout.</param>
    /// <param name="resolver">The resolver.</param>
    /// <param name="placed">Whether the hook placed the clusters; <see langword="false"/> on entry.</param>
    static partial void PlaceOnPaths(SvgTextLayout layout, SvgResolver resolver, ref bool placed);
}
