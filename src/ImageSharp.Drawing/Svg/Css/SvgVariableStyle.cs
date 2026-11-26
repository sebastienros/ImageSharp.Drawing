// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The custom property declarations (<c>--name: value</c>) and the pending <c>var()</c> declarations of one specified
/// style, which <see cref="SvgExtendedStyle.Variables"/> holds.
/// </summary>
/// <remarks>
/// <para>
/// Custom properties cascade by name: a declaration of higher precedence replaces one of lower precedence with the same
/// name, and names are case-sensitive. A pending declaration is the winning declaration of its property, unless a
/// declaration of higher precedence without <c>var()</c> follows it; see <see cref="SvgSpecifiedStyle"/>.
/// </para>
/// <para>
/// Ownership follows <see cref="SvgExtendedStyle"/>: the parser changes the instance of the style it fills, and
/// <see cref="Merge"/> and <see cref="WithoutPending"/> build new instances.
/// </para>
/// </remarks>
internal sealed class SvgVariableStyle
{
    // The custom property declarations by name, created on the first one.
    private Dictionary<string, SvgCustomPropertyValue>? custom;

    // The pending declarations, at most one per property: the set bits of Pending.
    private SvgPendingValue[] pending = [];

    /// <summary>
    /// Gets the properties with a pending declaration.
    /// </summary>
    public SvgPropertySet Pending { get; private set; }

    /// <summary>
    /// Gets the custom property declarations by name, or <see langword="null"/> when there are none.
    /// </summary>
    public IReadOnlyDictionary<string, SvgCustomPropertyValue>? CustomProperties => this.custom;

    /// <summary>
    /// Gets the pending declarations, one per property in <see cref="Pending"/>, in no particular order.
    /// </summary>
    public ReadOnlySpan<SvgPendingValue> PendingValues => this.pending;

    /// <summary>
    /// Gets a value indicating whether the instance holds nothing.
    /// </summary>
    private bool IsEmpty => this.Pending.IsEmpty && (this.custom is null || this.custom.Count == 0);

    /// <summary>
    /// Returns the pending declaration of a property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the declaration.</param>
    /// <returns><see langword="true"/> if the property has a pending declaration; otherwise, <see langword="false"/>.</returns>
    public bool TryGetPending(SvgPropertyId property, out SvgPendingValue value)
    {
        if (this.Pending.Contains(property))
        {
            foreach (SvgPendingValue entry in this.pending)
            {
                if (entry.Property == property)
                {
                    value = entry;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Records a custom property declaration. A later declaration of the same name replaces it.
    /// </summary>
    /// <param name="name">The name, with its leading <c>--</c>.</param>
    /// <param name="value">The value.</param>
    public void SetCustomProperty(string name, SvgCustomPropertyValue value)
    {
        this.custom ??= new Dictionary<string, SvgCustomPropertyValue>(StringComparer.Ordinal);
        this.custom[name] = value;
    }

    /// <summary>
    /// Records the pending declaration of a property, replacing an earlier one.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="text">The value text.</param>
    /// <param name="shorthand">The shorthand of the declaration, or <see langword="null"/>.</param>
    public void SetPending(SvgPropertyId property, string text, SvgShorthand? shorthand)
    {
        this.RemovePending(property);

        // The array holds at most one entry per property, so it stays small and each change copies it.
        SvgPendingValue[] values = new SvgPendingValue[this.pending.Length + 1];
        this.pending.CopyTo(values, 0);
        values[^1] = new SvgPendingValue(property, text, shorthand);
        this.pending = values;
        this.Pending |= SvgPropertySet.Of(property);
    }

    /// <summary>
    /// Removes the pending declaration of a property, if it has one.
    /// </summary>
    /// <param name="property">The property.</param>
    public void RemovePending(SvgPropertyId property)
    {
        if (!this.Pending.Contains(property))
        {
            return;
        }

        this.pending = Array.FindAll(this.pending, entry => entry.Property != property);
        this.Pending &= ~SvgPropertySet.Of(property);
    }

    /// <summary>
    /// Returns a copy without the pending declarations of some properties, or <see langword="null"/> when nothing
    /// remains. This instance does not change.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns>The copy, or <see langword="null"/>.</returns>
    public SvgVariableStyle? WithoutPending(SvgPropertySet properties)
    {
        SvgVariableStyle result = this.Clone();
        result.RemovePendingSet(properties);
        return result.IsEmpty ? null : result;
    }

    /// <summary>
    /// Returns the values of a style of lower precedence with those of a style of higher precedence applied over them:
    /// the custom properties of both, the source winning per name; the pending declarations of the target that the source
    /// does not replace; and the pending declarations of the source. Neither input changes.
    /// </summary>
    /// <param name="target">The values of lower precedence, or <see langword="null"/>.</param>
    /// <param name="source">The values of higher precedence, or <see langword="null"/>.</param>
    /// <param name="properties">The properties that the style of higher precedence specifies; their pending declarations in <paramref name="target"/> lose.</param>
    /// <returns>The merged values, or <see langword="null"/> when nothing remains.</returns>
    public static SvgVariableStyle? Merge(SvgVariableStyle? target, SvgVariableStyle? source, SvgPropertySet properties)
    {
        if (target is null && source is not null)
        {
            return source;
        }

        SvgVariableStyle result = target is null ? new SvgVariableStyle() : target.Clone();
        result.RemovePendingSet(properties);
        if (source is not null)
        {
            if (source.custom is not null)
            {
                foreach (KeyValuePair<string, SvgCustomPropertyValue> entry in source.custom)
                {
                    result.SetCustomProperty(entry.Key, entry.Value);
                }
            }

            foreach (SvgPendingValue entry in source.pending)
            {
                result.SetPending(entry.Property, entry.Text, entry.Shorthand);
            }
        }

        return result.IsEmpty ? null : result;
    }

    /// <summary>
    /// Returns a copy that the caller may change. The custom property map is copied, so the copy owns it.
    /// </summary>
    /// <returns>The copy.</returns>
    private SvgVariableStyle Clone()
    {
        SvgVariableStyle copy = (SvgVariableStyle)this.MemberwiseClone();
        if (this.custom is not null)
        {
            copy.custom = new Dictionary<string, SvgCustomPropertyValue>(this.custom, StringComparer.Ordinal);
        }

        return copy;
    }

    /// <summary>
    /// Removes the pending declarations of a set of properties.
    /// </summary>
    /// <param name="properties">The properties.</param>
    private void RemovePendingSet(SvgPropertySet properties)
    {
        SvgPropertySet removed = this.Pending & properties;
        if (removed.IsEmpty)
        {
            return;
        }

        this.pending = Array.FindAll(this.pending, entry => !removed.Contains(entry.Property));
        this.Pending &= ~removed;
    }
}
