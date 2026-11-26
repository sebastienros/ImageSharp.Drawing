// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The computed custom properties of an element: the inherited map from names (<c>--name</c>) to token text, which
/// <see cref="SvgComputedStyle.CustomProperties"/> holds.
/// </summary>
/// <remarks>
/// <para>
/// Custom properties inherit, so most elements have the map of their parent. An instance holds the declarations of one
/// element and refers to the map of its parent; an element that declares no custom property shares the instance of its
/// parent, so the common case allocates nothing. Lookups walk the chain of instances. The chain holds only elements that
/// declare custom properties, and it is flattened every <see cref="MaxDepth"/> levels: when a child would make it longer,
/// the parent builds, once, a single map of everything it sees, and the child refers to that. A lookup therefore probes
/// at most <see cref="MaxDepth"/> maps, and the total work of building maps grows with the number of declarations times
/// the number of flattenings, not with the number of elements.
/// </para>
/// <para>
/// The values are computed values: <c>var()</c> references inside a custom property are substituted at the element that
/// declares it, as CSS Custom Properties Level 1 defines, so a descendant that redeclares a referenced property does not
/// change the value it inherits. A name whose value is <see langword="null"/> holds the guaranteed-invalid value (the
/// <c>initial</c> keyword, a reference that cannot be substituted, or a member of a reference cycle), which hides a value
/// of an ancestor.
/// </para>
/// <para>
/// Instances never change after they are created, apart from the cache of the flattened map, which the single-threaded
/// resolve stage fills.
/// </para>
/// </remarks>
internal sealed class SvgCustomProperties : SvgVariableSubstitution.ISource
{
    /// <summary>
    /// The longest chain of instances that a lookup walks.
    /// </summary>
    public const int MaxDepth = 16;

    private readonly Dictionary<string, string?> values;
    private readonly Dictionary<string, string?>.AlternateLookup<ReadOnlySpan<char>> lookup;
    private readonly SvgCustomProperties? parent;

    // The flattened view of this instance and its ancestors, built when a child would exceed MaxDepth.
    private SvgCustomProperties? flattened;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCustomProperties"/> class.
    /// </summary>
    /// <param name="values">The values of this level, which the instance takes over.</param>
    /// <param name="parent">The map of the parent, or <see langword="null"/>.</param>
    private SvgCustomProperties(Dictionary<string, string?> values, SvgCustomProperties? parent)
    {
        this.values = values;
        this.lookup = values.GetAlternateLookup<ReadOnlySpan<char>>();
        this.parent = parent;
        this.Depth = parent is null ? 1 : parent.Depth + 1;
    }

    /// <summary>
    /// Gets the number of instances in the chain that ends at this one.
    /// </summary>
    public int Depth { get; }

    /// <summary>
    /// Computes the custom properties of an element from its declarations and the map of its parent.
    /// </summary>
    /// <param name="parent">The computed custom properties of the parent, or <see langword="null"/>.</param>
    /// <param name="declarations">The cascaded custom property declarations of the element, or <see langword="null"/>.</param>
    /// <param name="budget">The substitution budget of the document, or <see langword="null"/> for a budget of this call alone.</param>
    /// <returns>The computed map, <paramref name="parent"/> when the element declares nothing that changes it.</returns>
    public static SvgCustomProperties? Compute(
        SvgCustomProperties? parent,
        IReadOnlyDictionary<string, SvgCustomPropertyValue>? declarations,
        SvgSubstitutionBudget? budget = null)
    {
        if (declarations is null)
        {
            return parent;
        }

        Dictionary<string, string?>? own = null;
        foreach (KeyValuePair<string, SvgCustomPropertyValue> declaration in declarations)
        {
            // inherit and unset take the value of the parent, which a lookup finds anyway.
            if (declaration.Value.Kind == SvgCustomPropertyKind.Inherit)
            {
                continue;
            }

            own ??= new Dictionary<string, string?>(StringComparer.Ordinal);
            own[declaration.Key] = declaration.Value.Kind == SvgCustomPropertyKind.Initial ? null : declaration.Value.Text;
        }

        if (own is null)
        {
            return parent;
        }

        SvgCustomPropertyResolver.SubstituteDeclarations(own, parent, budget ?? new SvgSubstitutionBudget());

        if (parent is not null && parent.Depth >= MaxDepth)
        {
            parent = parent.Flatten();
        }

        return new SvgCustomProperties(own, parent);
    }

    /// <summary>
    /// Looks up a custom property.
    /// </summary>
    /// <param name="name">The name, with its leading <c>--</c>.</param>
    /// <param name="value">
    /// When this method returns <see langword="true"/>, contains the token text, or <see langword="null"/> for the
    /// guaranteed-invalid value.
    /// </param>
    /// <returns><see langword="true"/> if the element or an ancestor declares the name; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(ReadOnlySpan<char> name, out string? value)
    {
        for (SvgCustomProperties? level = this; level is not null; level = level.parent)
        {
            if (level.lookup.TryGetValue(name, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <inheritdoc/>
    bool SvgVariableSubstitution.ISource.TryGetValue(ReadOnlySpan<char> name, int depth, out string? value)
        => this.TryGetValue(name, out value);

    /// <summary>
    /// Returns an instance without a parent that holds every name this instance sees, built once and cached.
    /// </summary>
    /// <returns>The flattened instance.</returns>
    private SvgCustomProperties Flatten()
    {
        if (this.flattened is not null)
        {
            return this.flattened;
        }

        // Walk from this level up; a name seen at a lower level hides the same name further up.
        Dictionary<string, string?> all = new(StringComparer.Ordinal);
        for (SvgCustomProperties? level = this; level is not null; level = level.parent)
        {
            foreach (KeyValuePair<string, string?> entry in level.values)
            {
                _ = all.TryAdd(entry.Key, entry.Value);
            }
        }

        this.flattened = new SvgCustomProperties(all, null);
        return this.flattened;
    }
}
