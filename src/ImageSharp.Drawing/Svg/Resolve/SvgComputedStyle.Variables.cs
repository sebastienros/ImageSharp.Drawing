// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <content>
/// <c>var()</c> substitution at computed-value time.
/// </content>
internal partial struct SvgComputedStyle
{
    /// <summary>
    /// Computes the properties whose winning declaration contains <c>var()</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each pending declaration gets the computed custom properties of the element substituted in
    /// (<see cref="SvgVariableSubstitution"/>) and is parsed again, as its property or, for a longhand of a shorthand, as
    /// the shorthand. The results form a specified style that is applied over the specified style of the element without
    /// its pending declarations, and the element is computed again from that. Computing again keeps the order of
    /// <see cref="Compute"/>: a substituted <c>font-size</c> applies to the <c>em</c> lengths of the other properties.
    /// </para>
    /// <para>
    /// A substitution that fails, or a result that does not parse, makes the property invalid at computed-value time: it
    /// behaves as <c>unset</c>, so an inherited property takes the parent value and any other property its initial value,
    /// whatever declarations of lower precedence say. A malformed <c>var()</c> makes the declaration invalid at parse time
    /// instead, so the declaration of next lower precedence applies.
    /// </para>
    /// <para>
    /// Without a style context (<see cref="SvgUnitBasis.Context"/>), as outside a resolve, the pending declarations are left
    /// out and each such property keeps its declaration of next lower precedence.
    /// </para>
    /// </remarks>
    /// <param name="specified">The cascaded specified style of the element.</param>
    /// <param name="parent">The computed style of the parent.</param>
    /// <param name="initial">The initial style.</param>
    /// <param name="units">The document values that length units refer to.</param>
    /// <param name="kind">The kind of the element.</param>
    /// <param name="style">The computed style, which this method replaces.</param>
    static partial void SubstituteVariables(
        in SvgSpecifiedStyle specified,
        in SvgComputedStyle parent,
        in SvgComputedStyle initial,
        SvgUnitBasis units,
        SvgElementKind kind,
        ref SvgComputedStyle style)
    {
        SvgVariableStyle variables = specified.Extended!.Variables!;
        if (variables.Pending.IsEmpty || units.Context is not SvgStyleContext context)
        {
            return;
        }

        SvgCustomProperties? custom = style.CustomProperties;
        SvgVariableSubstitution.ISource source = (SvgVariableSubstitution.ISource?)custom ?? EmptySource.Instance;
        SvgPropertyParser parser = context.Parser;
        SvgSpecifiedStyle substituted = default;
        SvgPropertySet handled = default;
        ReadOnlySpan<SvgPendingValue> pending = variables.PendingValues;

        // Math functions in substituted values go to the table of the context, which the resolve stage may change.
        SvgCalcTable? previous = SvgCalcTable.Enter(context.SubstitutionCalc);
        try
        {
            foreach (SvgPendingValue entry in pending)
            {
                if (handled.Contains(entry.Property))
                {
                    continue;
                }

                SvgVariableSubstitution.Outcome outcome = SvgVariableSubstitution.Substitute(entry.Text, source, context.Budget, 0, out string? text);
                if (outcome == SvgVariableSubstitution.Outcome.InvalidSyntax)
                {
                    // Invalid at parse time: the declaration of next lower precedence applies, which the specified style
                    // without its pending declarations holds.
                    handled |= SvgPropertySet.Of(entry.Property);
                    continue;
                }

                // A result that still has var(), which only a string can hide, would be pending again.
                bool valid = outcome == SvgVariableSubstitution.Outcome.Substituted && !SvgPropertyParser.ContainsVar(text);
                if (entry.Shorthand is SvgShorthand shorthand)
                {
                    // Every longhand of the shorthand has the same text; one parse sets them all.
                    valid = valid && parser.TryParseShorthand(shorthand, text, ref substituted);
                    foreach (SvgPendingValue longhand in pending)
                    {
                        if (longhand.Shorthand == shorthand && ReferenceEquals(longhand.Text, entry.Text))
                        {
                            handled |= SvgPropertySet.Of(longhand.Property);
                            if (!valid)
                            {
                                MarkUnset(longhand.Property, ref substituted);
                            }
                        }
                    }

                    continue;
                }

                handled |= SvgPropertySet.Of(entry.Property);
                if (!valid || !parser.TryParse(entry.Property, text, ref substituted))
                {
                    MarkUnset(entry.Property, ref substituted);
                }
            }
        }
        finally
        {
            SvgCalcTable.Exit(previous);
        }

        // A shorthand sets all its longhands, but a later declaration of a longhand without var() won over the shorthand
        // in the cascade; such a longhand keeps its declaration.
        substituted.Unmark(substituted.Specified & ~variables.Pending);

        // A dash array of the parser of the context indexes the dash table of the context.
        if (substituted.HasValue(SvgPropertyId.StrokeDashArray))
        {
            substituted.StrokeDashArray = SvgStyleContext.ToContextDashArray(substituted.StrokeDashArray);
        }

        SvgSpecifiedStyle merged = specified.WithoutVariables();
        merged.Apply(in substituted);
        SvgComputedStyle result = Compute(in merged, in parent, in initial, units, kind);
        result.CustomProperties = custom;
        style = result;
    }

    /// <summary>
    /// Marks a property as <c>unset</c>, the value of a declaration that is invalid at computed-value time.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="style">The style that receives the keyword.</param>
    private static void MarkUnset(SvgPropertyId property, ref SvgSpecifiedStyle style)
    {
        if (SvgSpecifiedStyle.IsInherited(property))
        {
            style.MarkInherit(property);
        }
        else
        {
            style.MarkInitial(property);
        }
    }

    /// <summary>
    /// The custom properties of an element that has none.
    /// </summary>
    private sealed class EmptySource : SvgVariableSubstitution.ISource
    {
        /// <summary>
        /// Gets the instance.
        /// </summary>
        public static EmptySource Instance { get; } = new();

        /// <inheritdoc/>
        public bool TryGetValue(ReadOnlySpan<char> name, int depth, out string? value)
        {
            value = null;
            return false;
        }
    }
}
