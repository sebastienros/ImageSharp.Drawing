// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Diagnostics.CodeAnalysis;
using SixLabors.Fonts;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// The baseline of each cluster from <c>dominant-baseline</c>, <c>alignment-baseline</c>, and <c>baseline-shift</c>.
/// </content>
/// <remarks>
/// <para>
/// The model is the one Chromium and resvg share (measured against Chromium with the fonts of the resvg suite):
/// </para>
/// <list type="bullet">
/// <item><description>The baseline of a cluster comes from the span that holds its characters: its
/// <c>alignment-baseline</c>, or for <c>auto</c> and <c>baseline</c> its <c>dominant-baseline</c>. The glyphs move down
/// by the height of that baseline above the alphabetic baseline, measured in the primary font of the span. The
/// <c>alignment-baseline</c> of an ancestor span does not apply.</description></item>
/// <item><description><c>baseline-shift</c> adds up through the spans from the span of the cluster to the
/// <c>text</c> element, which itself is left out: SVG and CSS apply the property to <c>tspan</c> and <c>textPath</c>
/// only. Chromium also shifts on <c>text</c>; resvg, the suite, and the specifications do not. <c>sub</c> and
/// <c>super</c> move by half the font height (ascent plus descent) of the primary font of the span that sets them, and
/// percentages refer to the font size of that span, as in Chromium; CSS leaves both to the user agent.</description></item>
/// </list>
/// <para>
/// Baseline heights follow Chromium, which derives them from the ascent, the descent, and the x-height of the font:
/// hanging is 0.8 of the ascent, mathematical half the ascent, middle half the x-height, central the middle of the
/// ascent and descent, the text edges the ascent and the descent, and ideographic the descent. Chromium ignores the
/// <c>BASE</c> table (measured with Source Sans Pro, which has one), and SixLabors.Fonts does not expose it publicly.
/// Chromium does not support the CSS values <c>top</c>, <c>center</c>, <c>bottom</c>, <c>text-top</c>, and
/// <c>text-bottom</c> in SVG; they act as if they were not set.
/// </para>
/// <para>
/// The offset is across the line in every writing mode, toward the bottom of the glyphs. The dominant baseline
/// <c>auto</c> is <c>central</c> in <c>vertical-rl</c> and <c>vertical-lr</c> text, as CSS Writing Modes defines and
/// Chromium applies, and alphabetic otherwise. Shaping already centres an upright glyph of vertical text on the pen
/// position, which is the central baseline, so the offset of an upright cluster is the height of its baseline less the
/// height of the central baseline. Measured against Chromium with Noto Sans KR: <c>baseline-shift</c> and the alignment
/// baselines move vertical text across the line, as here.
/// </para>
/// </remarks>
internal static partial class SvgTextPositioner
{
    /// <summary>
    /// The share of the ascent that Chromium uses for the hanging baseline.
    /// </summary>
    private const float HangingAscentRatio = 0.8F;

    /// <summary>
    /// The share of the ascent that Chromium uses for the x-height of a font that does not give one.
    /// </summary>
    private const float XHeightAscentRatio = 0.56F;

    /// <summary>
    /// The baselines that a span can align to.
    /// </summary>
    private enum BaselineKind
    {
        /// <summary>
        /// The dominant baseline <c>auto</c>: central in vertical text, alphabetic otherwise.
        /// </summary>
        Auto,

        /// <summary>
        /// The alphabetic baseline: no offset.
        /// </summary>
        Alphabetic,

        /// <summary>
        /// The ideographic baseline.
        /// </summary>
        Ideographic,

        /// <summary>
        /// The middle baseline: half the x-height.
        /// </summary>
        Middle,

        /// <summary>
        /// The central baseline: between the ascent and the descent.
        /// </summary>
        Central,

        /// <summary>
        /// The mathematical baseline.
        /// </summary>
        Mathematical,

        /// <summary>
        /// The hanging baseline.
        /// </summary>
        Hanging,

        /// <summary>
        /// The top edge of the text: the ascent.
        /// </summary>
        TextBeforeEdge,

        /// <summary>
        /// The bottom edge of the text: the descent.
        /// </summary>
        TextAfterEdge
    }

    /// <summary>
    /// Adds the offset of a cluster from the alphabetic baseline at the pen position.
    /// </summary>
    /// <param name="layout">The layout, with the spans in <see cref="SvgTextContent.Spans"/>.</param>
    /// <param name="cluster">The cluster, with its span and font.</param>
    /// <param name="shift">The offset in user units, positive toward the bottom of the glyphs.</param>
    static partial void ApplyBaselineShift(SvgTextLayout layout, in SvgTextCluster cluster, ref float shift)
    {
        SvgTextSpan[] spans = layout.Content.Spans;
        float offset = 0F;

        // baseline-shift is cumulative; span 0 is the text element, where the property does not apply.
        for (int i = cluster.Span; i > 0; i = spans[i].Parent)
        {
            SvgTextSpan span = spans[i];
            SvgTextStyle text = span.Style.Text;
            switch (text.BaselineShiftKind)
            {
                case SvgBaselineShiftKind.Sub:
                case SvgBaselineShiftKind.Super:
                    if (TryGetFontMetrics(layout, span, out FontMetrics? metrics, out float scale))
                    {
                        float half = (metrics.HorizontalMetrics.Ascender - metrics.HorizontalMetrics.Descender) * scale / 2F;
                        offset += text.BaselineShiftKind == SvgBaselineShiftKind.Sub ? half : -half;
                    }

                    break;
                case SvgBaselineShiftKind.Length:
                    SvgLength length = text.BaselineShift;
                    offset -= length.Unit == SvgLengthUnit.Percent ? length.Value * span.Style.FontSize / 100F : length.Value;
                    break;
            }
        }

        // The writing mode of the text element decides the orientation of the whole text, as in Chromium.
        SvgTextDirections? directions = layout.Content.Directions;
        bool central = directions?.WritingMode is SvgWritingMode.VerticalRl or SvgWritingMode.VerticalLr;
        BaselineKind baseline = GetBaseline(spans, cluster.Span);
        if (baseline == BaselineKind.Auto)
        {
            baseline = central ? BaselineKind.Central : BaselineKind.Alphabetic;
        }

        SvgTextSpan own = spans[cluster.Span];
        bool upright = central && (directions!.Flags[cluster.CharStart] & SvgTextDirections.Upright) != 0;
        if ((baseline != BaselineKind.Alphabetic || upright) && TryGetFontMetrics(layout, own, out FontMetrics? ownMetrics, out float ownScale))
        {
            offset += GetBaselineHeight(baseline, ownMetrics, ownScale);
            if (upright)
            {
                offset -= GetBaselineHeight(BaselineKind.Central, ownMetrics, ownScale);
            }
        }

        shift += offset;
    }

    /// <summary>
    /// Returns the baseline that the characters of a span align to: its <c>alignment-baseline</c>, or for <c>auto</c>
    /// and <c>baseline</c>, its <c>dominant-baseline</c>.
    /// </summary>
    /// <param name="spans">The spans.</param>
    /// <param name="index">The span index.</param>
    /// <returns>The baseline; <see cref="BaselineKind.Auto"/> for the default baseline of the writing mode.</returns>
    private static BaselineKind GetBaseline(SvgTextSpan[] spans, int index)
    {
        switch (spans[index].Style.Text.AlignmentBaseline)
        {
            case SvgAlignmentBaseline.BeforeEdge:
            case SvgAlignmentBaseline.TextBeforeEdge:
                return BaselineKind.TextBeforeEdge;
            case SvgAlignmentBaseline.Middle:
                return BaselineKind.Middle;
            case SvgAlignmentBaseline.Central:
                return BaselineKind.Central;
            case SvgAlignmentBaseline.AfterEdge:
            case SvgAlignmentBaseline.TextAfterEdge:
                return BaselineKind.TextAfterEdge;
            case SvgAlignmentBaseline.Ideographic:
                return BaselineKind.Ideographic;
            case SvgAlignmentBaseline.Alphabetic:
                return BaselineKind.Alphabetic;
            case SvgAlignmentBaseline.Hanging:
                return BaselineKind.Hanging;
            case SvgAlignmentBaseline.Mathematical:
                return BaselineKind.Mathematical;
        }

        // auto, baseline, and the values Chromium does not support use the dominant baseline. dominant-baseline inherits;
        // no-change and reset-size (SVG 1.1), and the values Chromium does not support, take the value of the parent.
        for (int i = index; i >= 0; i = spans[i].Parent)
        {
            switch (spans[i].Style.Text.DominantBaseline)
            {
                case SvgDominantBaseline.NoChange:
                case SvgDominantBaseline.ResetSize:
                case SvgDominantBaseline.TextTop:
                case SvgDominantBaseline.TextBottom:
                    continue;
                case SvgDominantBaseline.Ideographic:
                    return BaselineKind.Ideographic;
                case SvgDominantBaseline.Middle:
                    return BaselineKind.Middle;
                case SvgDominantBaseline.Central:
                    return BaselineKind.Central;
                case SvgDominantBaseline.Mathematical:
                    return BaselineKind.Mathematical;
                case SvgDominantBaseline.Hanging:
                    return BaselineKind.Hanging;
                case SvgDominantBaseline.TextBeforeEdge:
                    return BaselineKind.TextBeforeEdge;
                case SvgDominantBaseline.TextAfterEdge:
                    return BaselineKind.TextAfterEdge;
                case SvgDominantBaseline.Alphabetic:
                    return BaselineKind.Alphabetic;
                default:
                    // auto and use-script: the default baseline of the writing mode.
                    return BaselineKind.Auto;
            }
        }

        return BaselineKind.Auto;
    }

    /// <summary>
    /// Returns the height of a baseline above the alphabetic baseline.
    /// </summary>
    /// <param name="baseline">The baseline.</param>
    /// <param name="metrics">The metrics of the font.</param>
    /// <param name="scale">The scale from font units to user units.</param>
    /// <returns>The height in user units, positive upward.</returns>
    private static float GetBaselineHeight(BaselineKind baseline, FontMetrics metrics, float scale)
    {
        float ascent = metrics.HorizontalMetrics.Ascender * scale;
        float descent = -metrics.HorizontalMetrics.Descender * scale;
        switch (baseline)
        {
            case BaselineKind.TextBeforeEdge:
                return ascent;
            case BaselineKind.TextAfterEdge:
                return -descent;
            case BaselineKind.Central:
                return (ascent - descent) / 2F;
            case BaselineKind.Middle:
                return (metrics.XHeight > 0 ? metrics.XHeight * scale : ascent * XHeightAscentRatio) / 2F;
            case BaselineKind.Hanging:
                return ascent * HangingAscentRatio;
            case BaselineKind.Mathematical:
                return ascent / 2F;
            case BaselineKind.Ideographic:
                return -descent;
            default:
                return 0F;
        }
    }

    /// <summary>
    /// Gets the metrics of the primary font of a span at its font size.
    /// </summary>
    /// <param name="layout">The layout, for the font matcher.</param>
    /// <param name="span">The span.</param>
    /// <param name="metrics">When this method returns <see langword="true"/>, contains the metrics of the primary face.</param>
    /// <param name="scale">When this method returns <see langword="true"/>, contains the scale from font units to user units.</param>
    /// <returns><see langword="true"/> if the span has a font and a positive font size; otherwise, <see langword="false"/>.</returns>
    private static bool TryGetFontMetrics(SvgTextLayout layout, SvgTextSpan span, [NotNullWhen(true)] out FontMetrics? metrics, out float scale)
    {
        SvgComputedStyle style = span.Style;
        SvgFontFace? face = layout.FontMatcher.GetChain(style.Text).Primary;
        if (face is null || !(style.FontSize > 0F))
        {
            metrics = null;
            scale = 0F;
            return false;
        }

        metrics = face.Metrics;
        scale = style.FontSize / metrics.UnitsPerEm;
        return true;
    }
}
