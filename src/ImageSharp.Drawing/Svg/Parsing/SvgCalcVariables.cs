// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Svg;

/// <summary>
/// Named numbers that a math function may use, such as the channel keywords <c>r</c>, <c>g</c>, <c>b</c>, and
/// <c>alpha</c> of a relative color (CSS Color Level 5). The default instance has none.
/// </summary>
internal readonly struct SvgCalcVariables
{
    private readonly string[]? names;
    private readonly double[]? values;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgCalcVariables"/> struct.
    /// </summary>
    /// <param name="names">The names, which the instance takes over. Names match ignoring ASCII case.</param>
    /// <param name="values">The value of each name, which the instance takes over.</param>
    public SvgCalcVariables(string[] names, double[] values)
    {
        this.names = names;
        this.values = values;
    }

    /// <summary>
    /// Looks up a name.
    /// </summary>
    /// <param name="name">The identifier.</param>
    /// <param name="value">When this method returns <see langword="true"/>, contains the value.</param>
    /// <returns><see langword="true"/> if the name is known; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(ReadOnlySpan<char> name, out double value)
    {
        if (this.names is not null)
        {
            for (int i = 0; i < this.names.Length; i++)
            {
                if (name.Equals(this.names[i], StringComparison.OrdinalIgnoreCase))
                {
                    value = this.values![i];
                    return true;
                }
            }
        }

        value = 0;
        return false;
    }
}
