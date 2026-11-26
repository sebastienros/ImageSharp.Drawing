// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Collections.Frozen;
using System.Xml;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// The name table of one document read. Known names come from one immutable set that every read shares; other names go
/// to a <see cref="NameTable"/> of the read.
/// </summary>
/// <remarks>
/// <para>
/// The reader matches element, attribute, and property names by reference against the constants of
/// <see cref="SvgNames"/>, so the XML reader must return those exact instances. Seeding a new <see cref="NameTable"/>
/// with every known name did that, but cost one entry object per name for each document, which is a large part of the
/// allocations of a small document. The shared set returns the constant instances without a per-document copy.
/// </para>
/// <para>
/// <see cref="XmlNameTable"/> requires that equal names return the same instance for the life of the table. A known name
/// always returns its constant, and every other name is atomized by the per-document table, so the requirement holds.
/// The shared set is never changed after it is built, so concurrent reads are safe.
/// </para>
/// </remarks>
internal sealed class SvgNameTable : XmlNameTable
{
    private static readonly FrozenSet<string> Known = CreateKnownNames();
    private static readonly FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> KnownLookup = Known.GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly NameTable others = new();

    /// <inheritdoc/>
    public override string Add(char[] array, int offset, int length)
        => KnownLookup.TryGetValue(array.AsSpan(offset, length), out string? name) ? name : this.others.Add(array, offset, length);

    /// <inheritdoc/>
    public override string Add(string array)
        => Known.TryGetValue(array, out string? name) ? name : this.others.Add(array);

    /// <inheritdoc/>
    public override string? Get(char[] array, int offset, int length)
        => KnownLookup.TryGetValue(array.AsSpan(offset, length), out string? name) ? name : this.others.Get(array, offset, length);

    /// <inheritdoc/>
    public override string? Get(string array)
        => Known.TryGetValue(array, out string? name) ? name : this.others.Get(array);

    /// <summary>
    /// Creates the set of known names from the constant instances that the maps of <see cref="SvgNames"/> use as keys.
    /// </summary>
    /// <returns>The set, compared by ordinal value.</returns>
    private static FrozenSet<string> CreateKnownNames()
    {
        HashSet<string> names = new(StringComparer.Ordinal)
        {
            SvgNames.SvgNamespace,
            SvgNames.XLinkNamespace,
            SvgNames.XmlNamespace,
            SvgNames.Space,
            SvgNames.TextDecoration
        };
        foreach (object name in SvgNames.ElementKinds.Keys)
        {
            _ = names.Add((string)name);
        }

        foreach (object name in SvgNames.Attributes.Keys)
        {
            _ = names.Add((string)name);
        }

        foreach (object name in SvgNames.PresentationAttributes.Keys)
        {
            _ = names.Add((string)name);
        }

        return names.ToFrozenSet(StringComparer.Ordinal);
    }
}
