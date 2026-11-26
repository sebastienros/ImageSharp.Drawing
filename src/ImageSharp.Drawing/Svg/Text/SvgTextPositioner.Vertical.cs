// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Package T4 in stage 4: the pen advance of vertical text and of cursive scripts.
/// </summary>
/// <remarks>
/// <para>
/// In vertical text the pen advances downward (upward for <c>sideways-lr</c>). An upright cluster keeps the vertical
/// advance and the offsets that shaping gives it: SixLabors.Fonts centres the glyph on the pen position horizontally and
/// hangs it below the pen from its vertical origin, with the vertical metrics of the font where it has them and metrics
/// synthesized from the horizontal ones otherwise. A sideways cluster turns 90 degrees clockwise about its pen position
/// (counterclockwise for <c>sideways-lr</c>). In <c>vertical-rl</c> and <c>vertical-lr</c> text the dominant baseline is
/// <c>central</c>, as CSS Writing Modes defines and Chromium applies: a sideways cluster moves left by half the sum of the
/// ascent and the (negative) descent of the primary font of its span (<see cref="SvgTextDirections.CentralBaselines"/>),
/// so that the middle of the em box of that font lies on the pen position. The <c>sideways</c> values keep the
/// alphabetic baseline.
/// </para>
/// <para>
/// Cursive scripts: CSS Text Level 3 does not apply <c>letter-spacing</c> between the letters of a cursive script, such
/// as Arabic, and Chromium does not either, so the advance of such a cluster drops the letter spacing that stage 4 added.
/// </para>
/// </remarks>
internal static partial class SvgTextPositioner
{
    /// <inheritdoc cref="ApplyVerticalAdvance"/>
    static partial void ApplyVerticalAdvance(SvgTextLayout layout, ref SvgTextCluster cluster, ref bool vertical, ref float advance)
    {
        SvgTextContent content = layout.Content;
        SvgComputedStyle style = content.Spans[cluster.Span].Style;
        SvgLength letterSpacing = style.Text.LetterSpacing;
        if (letterSpacing.IsSpecified
            && letterSpacing.Value != 0F
            && SvgTextDirections.IsCursive(content.Text.AsSpan(cluster.CharStart, cluster.CharEnd - cluster.CharStart)))
        {
            advance -= letterSpacing.Unit == SvgLengthUnit.Percent ? letterSpacing.Value * style.FontSize / 100F : letterSpacing.Value;
        }

        SvgTextDirections? directions = content.Directions;
        if (directions is null || !directions.IsVertical)
        {
            return;
        }

        vertical = true;
        if ((directions.Flags[cluster.CharStart] & SvgTextDirections.Upright) != 0)
        {
            return;
        }

        SvgWritingMode mode = directions.WritingMode;
        if (mode == SvgWritingMode.SidewaysLr)
        {
            cluster.Rotation -= 90F;
            advance = -advance;
            return;
        }

        cluster.Rotation += 90F;
        if (directions.CentralBaselines is float[] centralBaselines)
        {
            cluster.X -= centralBaselines[cluster.Span];
        }
    }
}
