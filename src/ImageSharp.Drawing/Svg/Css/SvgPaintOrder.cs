// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The value of the <c>paint-order</c> property: the order in which a shape paints its fill, its stroke, and its markers. Each member names the three layers from the first painted to the last.
/// </summary>
/// <remarks>
/// CSS lists only the layers that change position: a layer that the value omits paints after the listed ones, in the order fill, stroke, markers. So <c>stroke</c> is <see cref="StrokeFillMarkers"/> and <c>markers stroke</c> is <see cref="MarkersStrokeFill"/>.
/// </remarks>
internal enum SvgPaintOrder : byte
{
    /// <summary>
    /// <c>normal</c>, the initial value: fill, then stroke, then markers.
    /// </summary>
    FillStrokeMarkers,

    /// <summary>
    /// Fill, then markers, then stroke.
    /// </summary>
    FillMarkersStroke,

    /// <summary>
    /// Stroke, then fill, then markers.
    /// </summary>
    StrokeFillMarkers,

    /// <summary>
    /// Stroke, then markers, then fill.
    /// </summary>
    StrokeMarkersFill,

    /// <summary>
    /// Markers, then fill, then stroke.
    /// </summary>
    MarkersFillStroke,

    /// <summary>
    /// Markers, then stroke, then fill.
    /// </summary>
    MarkersStrokeFill
}
