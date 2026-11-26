// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Numerics;
using System.Runtime.CompilerServices;

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// A set of <see cref="SvgPropertyId"/> values: a 128-bit mask in two words, where the bit index of a property is its
/// <see cref="SvgPropertyId"/> value.
/// </summary>
/// <remarks>
/// <see cref="Low"/> holds the properties 0 to 63 and <see cref="High"/> the properties 64 to 127. Every property that
/// <see cref="SvgSpecifiedStyle"/> stores inline has an id below 64, so the hot paths of the cascade work on
/// <see cref="Low"/> alone; see <see cref="SvgSpecifiedStyle"/> for where the high word lives.
/// </remarks>
internal readonly struct SvgPropertySet : IEquatable<SvgPropertySet>
{
    /// <summary>
    /// The number of properties that a set can hold.
    /// </summary>
    public const int Capacity = 128;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgPropertySet"/> struct.
    /// </summary>
    /// <param name="low">The bits of the properties 0 to 63.</param>
    /// <param name="high">The bits of the properties 64 to 127.</param>
    public SvgPropertySet(ulong low, ulong high)
    {
        this.Low = low;
        this.High = high;
    }

    /// <summary>
    /// Gets the empty set.
    /// </summary>
    public static SvgPropertySet Empty => default;

    /// <summary>
    /// Gets the bits of the properties 0 to 63.
    /// </summary>
    public ulong Low { get; }

    /// <summary>
    /// Gets the bits of the properties 64 to 127.
    /// </summary>
    public ulong High { get; }

    /// <summary>
    /// Gets a value indicating whether the set is empty.
    /// </summary>
    public bool IsEmpty => (this.Low | this.High) == 0;

    /// <summary>
    /// Returns the union of two sets.
    /// </summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns>The union.</returns>
    public static SvgPropertySet operator |(SvgPropertySet left, SvgPropertySet right)
        => new(left.Low | right.Low, left.High | right.High);

    /// <summary>
    /// Returns the intersection of two sets.
    /// </summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns>The intersection.</returns>
    public static SvgPropertySet operator &(SvgPropertySet left, SvgPropertySet right)
        => new(left.Low & right.Low, left.High & right.High);

    /// <summary>
    /// Returns the complement of a set.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <returns>Every property that is not in <paramref name="set"/>.</returns>
    public static SvgPropertySet operator ~(SvgPropertySet set) => new(~set.Low, ~set.High);

    /// <summary>
    /// Returns whether two sets hold the same properties.
    /// </summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><see langword="true"/> if the sets are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(SvgPropertySet left, SvgPropertySet right) => left.Equals(right);

    /// <summary>
    /// Returns whether two sets differ.
    /// </summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><see langword="true"/> if the sets differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(SvgPropertySet left, SvgPropertySet right) => !left.Equals(right);

    /// <summary>
    /// Returns the set that holds one property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>The set.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SvgPropertySet Of(SvgPropertyId property)
        => (int)property < 64 ? new SvgPropertySet(1UL << (int)property, 0) : new SvgPropertySet(0, 1UL << ((int)property - 64));

    /// <summary>
    /// Returns the set of the properties from <paramref name="first"/> to <paramref name="last"/>, both included.
    /// </summary>
    /// <param name="first">The first property.</param>
    /// <param name="last">The last property; not less than <paramref name="first"/>.</param>
    /// <returns>The set.</returns>
    public static SvgPropertySet Range(SvgPropertyId first, SvgPropertyId last)
    {
        ulong low = 0;
        ulong high = 0;
        for (int i = (int)first; i <= (int)last; i++)
        {
            if (i < 64)
            {
                low |= 1UL << i;
            }
            else
            {
                high |= 1UL << (i - 64);
            }
        }

        return new SvgPropertySet(low, high);
    }

    /// <summary>
    /// Returns whether the set holds a property.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns><see langword="true"/> if the property is in the set; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(SvgPropertyId property)
        => (int)property < 64
            ? (this.Low & (1UL << (int)property)) != 0
            : (this.High & (1UL << ((int)property - 64))) != 0;

    /// <summary>
    /// Returns whether the set shares a property with another set.
    /// </summary>
    /// <param name="other">The other set.</param>
    /// <returns><see langword="true"/> if the sets overlap; otherwise, <see langword="false"/>.</returns>
    public bool Overlaps(SvgPropertySet other) => ((this.Low & other.Low) | (this.High & other.High)) != 0;

    /// <summary>
    /// Returns the property with the lowest id of a non-empty set and removes it, for a loop that visits the set bits.
    /// </summary>
    /// <param name="set">The set; it must not be empty. It loses its first property.</param>
    /// <returns>The property.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static SvgPropertyId TakeFirst(ref SvgPropertySet set)
    {
        ulong low = set.Low;
        if (low != 0)
        {
            set = new SvgPropertySet(low & (low - 1), set.High);
            return (SvgPropertyId)BitOperations.TrailingZeroCount(low);
        }

        ulong high = set.High;
        set = new SvgPropertySet(0, high & (high - 1));
        return (SvgPropertyId)(64 + BitOperations.TrailingZeroCount(high));
    }

    /// <inheritdoc/>
    public bool Equals(SvgPropertySet other) => this.Low == other.Low && this.High == other.High;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is SvgPropertySet other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(this.Low, this.High);
}
