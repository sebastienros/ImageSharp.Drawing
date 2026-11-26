// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// The test collection of SVG tests that run alone, after the tests that run in parallel: tests that load many documents
/// and tests that compare times. Running them alone keeps their load away from the timing comparisons of other classes,
/// and keeps the load of other classes away from their own.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SvgSequentialCollection
{
    /// <summary>
    /// The name of the collection.
    /// </summary>
    public const string Name = "SVG tests that run alone";
}
