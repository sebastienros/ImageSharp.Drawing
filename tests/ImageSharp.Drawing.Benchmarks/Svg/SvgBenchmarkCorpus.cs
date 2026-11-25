// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Text;
using SixLabors.ImageSharp.Drawing.Tests;

namespace SixLabors.ImageSharp.Drawing.Benchmarks.Svg;

/// <summary>
/// The named entries of the SVG benchmark corpus. An entry is one document or a set of documents
/// that a benchmark processes together in one operation.
/// </summary>
internal static class SvgBenchmarkCorpus
{
    /// <summary>
    /// The root start tag of <c>paris-30k.svg</c>. The file has no size and no viewBox.
    /// </summary>
    private const string ParisRootTag = "<svg xmlns=\"http://www.w3.org/2000/svg\">";

    /// <summary>
    /// The root start tag that gives <c>paris-30k.svg</c> the viewport of its content.
    /// The content is a 1096.25 x 1060 map. The viewport is 1096 x 1060, the area that the <c>FillParis</c>
    /// benchmark draws. Whole numbers matter: some renderers round the intrinsic size to whole pixels
    /// before they scale it, which changes the output size.
    /// </summary>
    private const string ParisSizedRootTag =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1096\" height=\"1060\" viewBox=\"0 0 1096 1060\">";

    /// <summary>
    /// Gets the entry names, in the order that the benchmark tables show them.
    /// </summary>
    public static IEnumerable<string> Names { get; } =
    [
        "Tiger",
        "Paris30k",
        "Icons",
        "SoftIceCream",
        "Moose",
        "StyledLandscape",
        "Dashboard",
        "PatternChart"
    ];

    /// <summary>
    /// Gets the input files of an entry, relative to the test image input directory.
    /// </summary>
    /// <param name="name">The entry name.</param>
    /// <returns>The relative file paths.</returns>
    public static string[] GetFiles(string name) => name switch
    {
        "Tiger" => [TestImages.Svg.GhostscriptTiger],
        "Paris30k" => [TestImages.Svg.Paris30k],
        "Icons" => TestImages.Svg.Icons,
        "SoftIceCream" => [TestImages.Svg.FluentSoftIceCream],
        "Moose" => [TestImages.Svg.FluentMoose],
        "StyledLandscape" => [TestImages.Svg.StyledLandscape],
        "Dashboard" => [TestImages.Svg.CompositingDashboard],
        "PatternChart" => [TestImages.Svg.PatternChart],
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown SVG corpus entry.")
    };

    /// <summary>
    /// Reads the bytes of every document of an entry. Every renderer gets these exact bytes.
    /// </summary>
    /// <remarks>
    /// <c>paris-30k.svg</c> has no size and no viewBox, so SVG rules give it a 300 x 150 viewport
    /// that shows a corner of the map, and each renderer picks a different fallback size.
    /// The bytes get a viewport over the whole map so that every renderer draws the same area.
    /// </remarks>
    /// <param name="name">The entry name.</param>
    /// <returns>One byte array per document.</returns>
    public static byte[][] ReadDocuments(string name)
    {
        string[] files = GetFiles(name);
        byte[][] documents = new byte[files.Length][];
        for (int i = 0; i < files.Length; i++)
        {
            byte[] bytes = File.ReadAllBytes(TestFile.GetInputFileFullPath(files[i]));
            if (files[i] == TestImages.Svg.Paris30k)
            {
                string text = Encoding.UTF8.GetString(bytes);
                int index = text.IndexOf(ParisRootTag, StringComparison.Ordinal);
                if (index < 0)
                {
                    throw new InvalidOperationException("The root element of paris-30k.svg has changed.");
                }

                bytes = Encoding.UTF8.GetBytes(string.Concat(text.AsSpan(0, index), ParisSizedRootTag, text.AsSpan(index + ParisRootTag.Length)));
            }

            documents[i] = bytes;
        }

        return documents;
    }
}
