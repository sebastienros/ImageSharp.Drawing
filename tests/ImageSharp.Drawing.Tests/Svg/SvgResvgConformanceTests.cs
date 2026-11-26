// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

using System.Globalization;
using System.Numerics;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Drawing.Svg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace SixLabors.ImageSharp.Drawing.Tests.Svg;

/// <summary>
/// Runs the resvg conformance test suite and checks every outcome against the checked-in expected results in
/// <c>ResvgExpectedResults.txt</c>.
/// </summary>
/// <remarks>
/// <para>
/// The suite is not part of the repository. <c>node tests/SvgTools/fetch-resvg-tests.js</c> fetches it into
/// <c>tests/Images/External/resvg-test-suite</c>. Without it the test returns early.
/// </para>
/// <para>
/// Each test SVG draws into a transparent image of the size of its reference PNG, as
/// <c>canvas.DrawSvg(document, new RectangleF(0, 0, width, height))</c>. The pass criterion is in
/// <see cref="CountMismatchedPixels"/>.
/// </para>
/// </remarks>
public class SvgResvgConformanceTests
{
    /// <summary>
    /// The suite directory, relative to the repository root.
    /// </summary>
    private const string SuiteRelativePath = "tests/Images/External/resvg-test-suite";

    /// <summary>
    /// The expected results file, relative to the repository root.
    /// </summary>
    private const string ExpectedResultsRelativePath = "tests/ImageSharp.Drawing.Tests/Svg/ResvgExpectedResults.txt";

    /// <summary>
    /// The channel difference that a pixel may have outside the range of its neighbourhood in the other image, in 8-bit
    /// units of premultiplied RGBA. See <see cref="CountMismatchedPixels"/>.
    /// </summary>
    private const int ChannelTolerance = 24;

    /// <summary>
    /// The largest number of mismatched pixels that a passing test may have. See <see cref="CountMismatchedPixels"/>.
    /// </summary>
    private const int MaxMismatchedPixels = 40;

    /// <summary>
    /// The time limit for loading and drawing one test.
    /// </summary>
    private static readonly TimeSpan TestTimeLimit = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The fonts of the suite, from its <c>fonts</c> directory, loaded once in ordinal order of the file names, as the
    /// resvg test harness loads them.
    /// </summary>
    private static readonly Lazy<FontCollection> SuiteFonts = new(LoadSuiteFonts);

    private readonly ITestOutputHelper output;

    /// <summary>
    /// Initializes a new instance of the <see cref="SvgResvgConformanceTests"/> class.
    /// </summary>
    /// <param name="output">The test output.</param>
    public SvgResvgConformanceTests(ITestOutputHelper output) => this.output = output;

    /// <summary>
    /// The outcome of one test.
    /// </summary>
    private enum Outcome
    {
        /// <summary>
        /// The render matches the reference image.
        /// </summary>
        Pass,

        /// <summary>
        /// The render does not match the reference image, or the document did not load.
        /// </summary>
        Fail,

        /// <summary>
        /// The suite marks the test as undefined behavior. It does not run.
        /// </summary>
        Skip,

        /// <summary>
        /// Loading or drawing threw an exception other than <see cref="InvalidImageContentException"/>, or took longer
        /// than the time limit. This is a defect, whatever the expected result says.
        /// </summary>
        Defect
    }

    /// <summary>
    /// Runs every test of the suite and compares the outcomes with the expected results.
    /// </summary>
    [Fact]
    public void ResvgTestSuite_MatchesExpectedResults()
    {
        string suiteDirectory = TestEnvironment.GetFullPath(SuiteRelativePath);
        string testsDirectory = IOPath.Combine(suiteDirectory, "tests");
        if (!Directory.Exists(testsDirectory))
        {
            this.output.WriteLine(
                $"The resvg test suite is not in {suiteDirectory}. " +
                "Run 'node tests/SvgTools/fetch-resvg-tests.js' to fetch it. The test did not run.");
            return;
        }

        Dictionary<string, ExpectedResult> expected = ReadExpectedResults(TestEnvironment.GetFullPath(ExpectedResultsRelativePath));
        Dictionary<string, SuiteResult> suiteResults = ReadSuiteResults(IOPath.Combine(suiteDirectory, "results.csv"));
        string outputDirectory = TestEnvironment.CreateOutputDirectory("Svg", "Resvg");

        List<string> tests = [];
        foreach (string file in Directory.EnumerateFiles(testsDirectory, "*.svg", SearchOption.AllDirectories))
        {
            tests.Add(IOPath.GetRelativePath(testsDirectory, file).Replace('\\', '/'));
        }

        tests.Sort(StringComparer.Ordinal);

        List<string> mismatches = [];
        StringBuilder results = new();
        _ = results.AppendLine("path\toutcome\tmismatched pixels\tdetail");
        SortedDictionary<string, CategorySummary> summaries = new(StringComparer.Ordinal);

        foreach (string test in tests)
        {
            string category = test[..test.IndexOf('/')];
            if (!summaries.TryGetValue(category, out CategorySummary summary))
            {
                summary = new CategorySummary();
                summaries.Add(category, summary);
            }

            bool undefined = suiteResults.TryGetValue(test, out SuiteResult suiteResult) && suiteResult.Chrome == 0;
            if (!undefined)
            {
                summary.SuiteTests++;
                summary.ChromePassed += suiteResult.Chrome == 1 ? 1 : 0;
                summary.ResvgPassed += suiteResult.Resvg == 1 ? 1 : 0;
                summary.LibrsvgPassed += suiteResult.Librsvg == 1 ? 1 : 0;
            }

            (Outcome outcome, int mismatched, string detail) = undefined
                ? (Outcome.Skip, -1, "undefined behavior in the suite")
                : Run(IOPath.Combine(testsDirectory, test), IOPath.Combine(outputDirectory, test));

            _ = results.Append(test).Append('\t').Append(outcome).Append('\t')
                .Append(mismatched.ToString(CultureInfo.InvariantCulture)).Append('\t').AppendLine(detail);

            if (!expected.TryGetValue(test, out ExpectedResult expectedResult))
            {
                mismatches.Add($"{test}: {outcome} ({detail}), but the test is not in the expected results.");
                continue;
            }

            expectedResult.Seen = true;
            switch (outcome)
            {
                case Outcome.Pass:
                    summary.Passed++;
                    break;
                case Outcome.Skip:
                    summary.Skipped++;
                    break;
                default:
                    summary.CountFailure(expectedResult.ReasonGroup);
                    break;
            }

            if (outcome == Outcome.Defect)
            {
                mismatches.Add($"{test}: defect: {detail}");
            }
            else if (outcome != expectedResult.Outcome)
            {
                string reason = expectedResult.Reason.Length > 0 ? $" ({expectedResult.Reason})" : string.Empty;
                mismatches.Add($"{test}: expected {expectedResult.Outcome}{reason}, but the outcome is {outcome} ({detail}).");
            }
        }

        foreach (KeyValuePair<string, ExpectedResult> entry in expected)
        {
            if (!entry.Value.Seen)
            {
                mismatches.Add($"{entry.Key}: listed in the expected results, but not in the suite.");
            }
        }

        File.WriteAllText(IOPath.Combine(outputDirectory, "results.tsv"), results.ToString());
        this.WriteSummary(summaries, outputDirectory);

        foreach (string mismatch in mismatches)
        {
            this.output.WriteLine(mismatch);
        }

        string message = $"{mismatches.Count} tests do not match {ExpectedResultsRelativePath}. The test output lists them. " +
            "A listed pass that fails is a regression. A listed failure that passes must be promoted to pass in the list.";
        Assert.True(mismatches.Count == 0, message);
    }

    /// <summary>
    /// Loads and draws one test, and compares the render with the reference image. A render that does not pass is
    /// saved to the test output.
    /// </summary>
    /// <param name="svgPath">The full path of the test SVG.</param>
    /// <param name="outputPath">The path of the saved render, with the <c>.svg</c> extension.</param>
    /// <returns>The outcome, the number of mismatched pixels or -1, and a detail text.</returns>
    private static (Outcome Outcome, int Mismatched, string Detail) Run(string svgPath, string outputPath)
    {
        using Image<Rgba32> reference = Image.Load<Rgba32>(IOPath.ChangeExtension(svgPath, ".png"));
        Image<Rgba32> actual = new(reference.Width, reference.Height);
        try
        {
            // A hang cannot be stopped, but the time limit reports it and lets the remaining tests run.
            Task<Exception> task = Task.Run(() => LoadAndDraw(svgPath, actual));
            if (!task.Wait(TestTimeLimit))
            {
                actual = null;
                return (Outcome.Defect, -1, $"loading and drawing took longer than {TestTimeLimit.TotalSeconds} s");
            }

            switch (task.Result)
            {
                case null:
                    break;
                case InvalidImageContentException exception:
                    return (Outcome.Fail, -1, $"invalid content: {exception.Message}");
                case Exception exception:
                    return (Outcome.Defect, -1, $"{exception.GetType().Name}: {exception.Message}");
            }

            int mismatched = CountMismatchedPixels(reference, actual);
            if (mismatched <= MaxMismatchedPixels)
            {
                return (Outcome.Pass, mismatched, string.Empty);
            }

            _ = Directory.CreateDirectory(IOPath.GetDirectoryName(outputPath)!);
            actual.SaveAsPng(IOPath.ChangeExtension(outputPath, ".png"));
            return (Outcome.Fail, mismatched, $"{mismatched} mismatched pixels");
        }
        finally
        {
            actual?.Dispose();
        }
    }

    /// <summary>
    /// Loads the fonts of the suite. Font files are the <c>.ttf</c> and <c>.otf</c> files of the <c>fonts</c> directory.
    /// </summary>
    /// <returns>The collection; empty when the suite is missing.</returns>
    private static FontCollection LoadSuiteFonts()
    {
        FontCollection collection = new();
        string directory = IOPath.Combine(TestEnvironment.GetFullPath(SuiteRelativePath), "fonts");
        if (!Directory.Exists(directory))
        {
            return collection;
        }

        List<string> files = [];
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            string extension = IOPath.GetExtension(file);
            if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(file);
            }
        }

        files.Sort(StringComparer.Ordinal);
        foreach (string file in files)
        {
            _ = collection.Add(file);
        }

        return collection;
    }

    /// <summary>
    /// Creates the generic family map of the resvg test harness (<c>crates/resvg/tests/integration/main.rs</c> in the
    /// resvg repository): serif is Noto Serif, sans-serif Noto Sans, cursive Yellowtail, fantasy Sedgwick Ave Display, and
    /// monospace Noto Mono.
    /// </summary>
    /// <returns>The map.</returns>
    private static SvgGenericFontFamilies CreateSuiteGenericFamilies()
        => new()
        {
            Serif = ["Noto Serif"],
            SansSerif = ["Noto Sans"],
            Cursive = ["Yellowtail"],
            Fantasy = ["Sedgwick Ave Display"],
            Monospace = ["Noto Mono"],
        };

    /// <summary>
    /// Loads a test SVG and draws it into the whole image.
    /// </summary>
    /// <param name="svgPath">The full path of the test SVG.</param>
    /// <param name="image">The target image.</param>
    /// <returns>The exception that loading or drawing threw, or <see langword="null"/>.</returns>
    private static Exception LoadAndDraw(string svgPath, Image<Rgba32> image)
    {
        try
        {
            // Image files load from the directory of the test, as a caller that trusts that directory would set it.
            // References that leave it, such as ../../../resources, are refused by the security rules of the option.
            // Text uses the fonts of the suite with the generic families of the resvg test harness.
            SvgLoadOptions options = new()
            {
                ResourceBaseDirectory = IOPath.GetDirectoryName(svgPath),
                FontCollection = SuiteFonts.Value,
                GenericFontFamilies = CreateSuiteGenericFamilies(),
            };

            SvgDocument document = SvgDocument.Load(svgPath, options);
            image.Mutate(x => x.Paint(canvas => canvas.DrawSvg(document, new RectangleF(0, 0, image.Width, image.Height))));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>
    /// Counts the pixels where a render differs from the reference image by more than an antialiasing difference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both images are converted to premultiplied RGBA, so the color of a fully transparent pixel does not matter.
    /// </para>
    /// <para>
    /// A pixel of one image matches when each of its four channels lies within the range of that channel over the 3 x 3
    /// neighbourhood at the same position in the other image, widened by <see cref="ChannelTolerance"/>. A pixel is
    /// mismatched when it does not match in either direction. Rasterizers cover edge pixels differently and can place
    /// an antialiased edge up to one pixel apart; the value of such a pixel lies between its neighbours in the other
    /// image, so it matches. Inside a flat area the range has no width, so a color that is off by more than the
    /// tolerance is mismatched, and so is a shape that is missing, extra, or more than one pixel out of place.
    /// </para>
    /// <para>
    /// A test passes with at most <see cref="MaxMismatchedPixels"/> mismatched pixels. The calibration of both values
    /// with Chromium renders of the suite is described in the header of the expected results file.
    /// </para>
    /// </remarks>
    /// <param name="reference">The reference image.</param>
    /// <param name="actual">The render, of the same size.</param>
    /// <returns>The number of mismatched pixels.</returns>
    private static int CountMismatchedPixels(Image<Rgba32> reference, Image<Rgba32> actual)
    {
        int width = reference.Width;
        int height = reference.Height;
        Rgba32[] a = Premultiply(reference);
        Rgba32[] b = Premultiply(actual);

        int count = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Two pixels within the tolerance of each other always lie within the widened ranges.
                int i = (y * width) + x;
                if (Distance(a[i], b[i]) <= ChannelTolerance)
                {
                    continue;
                }

                Vector4 minA = new(255F);
                Vector4 maxA = Vector4.Zero;
                Vector4 minB = new(255F);
                Vector4 maxB = Vector4.Zero;
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                {
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                    {
                        int j = (ny * width) + nx;
                        Vector4 pa = ToVector(a[j]);
                        Vector4 pb = ToVector(b[j]);
                        minA = Vector4.Min(minA, pa);
                        maxA = Vector4.Max(maxA, pa);
                        minB = Vector4.Min(minB, pb);
                        maxB = Vector4.Max(maxB, pb);
                    }
                }

                Vector4 va = ToVector(a[i]);
                Vector4 vb = ToVector(b[i]);
                Vector4 excess = Vector4.Max(
                    Vector4.Max(minB - va, va - maxB),
                    Vector4.Max(minA - vb, vb - maxA));

                if (MathF.Max(MathF.Max(excess.X, excess.Y), MathF.Max(excess.Z, excess.W)) > ChannelTolerance)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Returns the channels of a pixel as a vector of 8-bit values.
    /// </summary>
    /// <param name="pixel">The pixel.</param>
    /// <returns>The vector.</returns>
    private static Vector4 ToVector(Rgba32 pixel) => new(pixel.R, pixel.G, pixel.B, pixel.A);

    /// <summary>
    /// Copies the pixels of an image into an array, premultiplied by alpha with rounding.
    /// </summary>
    /// <param name="image">The image.</param>
    /// <returns>The premultiplied pixels, row by row.</returns>
    private static Rgba32[] Premultiply(Image<Rgba32> image)
    {
        Rgba32[] pixels = new Rgba32[image.Width * image.Height];
        image.CopyPixelDataTo(pixels);
        for (int i = 0; i < pixels.Length; i++)
        {
            Rgba32 p = pixels[i];
            pixels[i] = new Rgba32(
                (byte)(((p.R * p.A) + 127) / 255),
                (byte)(((p.G * p.A) + 127) / 255),
                (byte)(((p.B * p.A) + 127) / 255),
                p.A);
        }

        return pixels;
    }

    /// <summary>
    /// Returns the largest absolute channel difference of two pixels.
    /// </summary>
    /// <param name="a">The first pixel.</param>
    /// <param name="b">The second pixel.</param>
    /// <returns>The distance, from 0 to 255.</returns>
    private static int Distance(Rgba32 a, Rgba32 b)
        => Math.Max(
            Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)),
            Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));

    /// <summary>
    /// Reads the expected results file. A line holds a test path, an outcome (<c>pass</c>, <c>fail</c>, or
    /// <c>skip</c>), and for <c>fail</c> and <c>skip</c> a reason. A reason starts with its group, followed by a colon.
    /// Empty lines and lines that start with <c>#</c> are ignored.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The expected results by test path.</returns>
    private static Dictionary<string, ExpectedResult> ReadExpectedResults(string path)
    {
        Dictionary<string, ExpectedResult> results = new(StringComparer.Ordinal);
        foreach (string rawLine in File.ReadLines(path))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Outcome outcome = parts[1] switch
            {
                "pass" => Outcome.Pass,
                "fail" => Outcome.Fail,
                "skip" => Outcome.Skip,
                _ => throw new InvalidDataException($"Unknown outcome '{parts[1]}' in line: {line}")
            };

            string reason = parts.Length > 2 ? parts[2] : string.Empty;
            if (outcome != Outcome.Pass && reason.Length == 0)
            {
                throw new InvalidDataException($"An expected failure or skip needs a reason: {line}");
            }

            results.Add(parts[0], new ExpectedResult(outcome, reason));
        }

        return results;
    }

    /// <summary>
    /// Reads the per-renderer results that the suite authors recorded in <c>results.csv</c>.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The results by test path.</returns>
    private static Dictionary<string, SuiteResult> ReadSuiteResults(string path)
    {
        Dictionary<string, SuiteResult> results = new(StringComparer.Ordinal);
        string[] lines = File.ReadAllLines(path);
        string[] header = lines[0].Split(',');
        int chrome = Array.IndexOf(header, "chrome");
        int resvg = Array.IndexOf(header, "resvg");
        int librsvg = Array.IndexOf(header, "librsvg");
        for (int i = 1; i < lines.Length; i++)
        {
            string[] fields = lines[i].Split(',');
            if (fields.Length < header.Length)
            {
                continue;
            }

            results[fields[0]] = new SuiteResult(
                int.Parse(fields[chrome], CultureInfo.InvariantCulture),
                int.Parse(fields[resvg], CultureInfo.InvariantCulture),
                int.Parse(fields[librsvg], CultureInfo.InvariantCulture));
        }

        return results;
    }

    /// <summary>
    /// Writes the per-category summary, with the pass counts that the suite authors recorded for three renderers, to the
    /// test output and to <c>summary.txt</c> in the output directory.
    /// </summary>
    /// <param name="summaries">The summaries by category.</param>
    /// <param name="outputDirectory">The output directory.</param>
    private void WriteSummary(SortedDictionary<string, CategorySummary> summaries, string outputDirectory)
    {
        StringBuilder text = new();
        CategorySummary total = new();
        _ = text.AppendLine("resvg test suite: outcomes per category. 'run' counts the tests that are not undefined behavior.")
            .AppendLine("The results.csv columns count the tests of 'run' that the suite authors recorded as passed by each renderer.")
            .AppendLine("Their criterion is a manual review, which differs from the pass criterion of this test.")
            .AppendLine(
                CultureInfo.InvariantCulture,
                $"{"category",-14} {"tests",6} {"skipped",8} {"run",5} {"passed",7} {"failed",7} | {"results.csv: resvg",19} {"chrome",7} {"librsvg",8}");
        foreach (KeyValuePair<string, CategorySummary> entry in summaries)
        {
            AppendRow(text, entry.Key, entry.Value);
            total.Add(entry.Value);
        }

        AppendRow(text, "total", total);
        _ = text.AppendLine().AppendLine("Failures per category and reason group of the expected results:");
        foreach (KeyValuePair<string, CategorySummary> entry in summaries)
        {
            foreach (KeyValuePair<string, int> group in entry.Value.FailuresByGroup)
            {
                _ = text.AppendLine(CultureInfo.InvariantCulture, $"{entry.Key,-14} {group.Value,5}  {group.Key}");
            }
        }

        string summary = text.ToString();
        File.WriteAllText(IOPath.Combine(outputDirectory, "summary.txt"), summary);
        this.output.WriteLine(summary);

        static void AppendRow(StringBuilder text, string name, CategorySummary s)
        {
            string ours = $"{name,-14} {s.Passed + s.Failed + s.Skipped,6} {s.Skipped,8} {s.SuiteTests,5} {s.Passed,7} {s.Failed,7}";
            _ = text.AppendLine(CultureInfo.InvariantCulture, $"{ours} | {s.ResvgPassed,19} {s.ChromePassed,7} {s.LibrsvgPassed,8}");
        }
    }

    /// <summary>
    /// An entry of the expected results file.
    /// </summary>
    private sealed class ExpectedResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExpectedResult"/> class.
        /// </summary>
        /// <param name="outcome">The expected outcome.</param>
        /// <param name="reason">The reason of an expected failure or skip.</param>
        public ExpectedResult(Outcome outcome, string reason)
        {
            this.Outcome = outcome;
            this.Reason = reason;
            int colon = reason.IndexOf(':');
            this.ReasonGroup = colon > 0 ? reason[..colon] : reason;
        }

        /// <summary>
        /// Gets the expected outcome.
        /// </summary>
        public Outcome Outcome { get; }

        /// <summary>
        /// Gets the reason of an expected failure or skip, or an empty string.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Gets the group of the reason: the text before the first colon.
        /// </summary>
        public string ReasonGroup { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the suite contains the test.
        /// </summary>
        public bool Seen { get; set; }
    }

    /// <summary>
    /// The outcomes of one category.
    /// </summary>
    private sealed class CategorySummary
    {
        /// <summary>
        /// Gets the number of failures by reason group of the expected results.
        /// </summary>
        public SortedDictionary<string, int> FailuresByGroup { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets the number of passed tests.
        /// </summary>
        public int Passed { get; set; }

        /// <summary>
        /// Gets the number of failed tests, defects included.
        /// </summary>
        public int Failed { get; private set; }

        /// <summary>
        /// Gets or sets the number of skipped tests.
        /// </summary>
        public int Skipped { get; set; }

        /// <summary>
        /// Gets or sets the number of tests that the suite does not mark as undefined behavior.
        /// </summary>
        public int SuiteTests { get; set; }

        /// <summary>
        /// Gets or sets the number of those tests that <c>results.csv</c> records as passed by Chrome.
        /// </summary>
        public int ChromePassed { get; set; }

        /// <summary>
        /// Gets or sets the number of those tests that <c>results.csv</c> records as passed by resvg.
        /// </summary>
        public int ResvgPassed { get; set; }

        /// <summary>
        /// Gets or sets the number of those tests that <c>results.csv</c> records as passed by librsvg.
        /// </summary>
        public int LibrsvgPassed { get; set; }

        /// <summary>
        /// Counts a failure in a reason group.
        /// </summary>
        /// <param name="group">The reason group of the expected result.</param>
        public void CountFailure(string group)
        {
            this.Failed++;
            group = group.Length > 0 ? group : "listed as pass";
            this.FailuresByGroup[group] = this.FailuresByGroup.GetValueOrDefault(group) + 1;
        }

        /// <summary>
        /// Adds the counts of another summary to this one.
        /// </summary>
        /// <param name="other">The other summary.</param>
        public void Add(CategorySummary other)
        {
            this.Passed += other.Passed;
            this.Failed += other.Failed;
            this.Skipped += other.Skipped;
            this.SuiteTests += other.SuiteTests;
            this.ChromePassed += other.ChromePassed;
            this.ResvgPassed += other.ResvgPassed;
            this.LibrsvgPassed += other.LibrsvgPassed;
        }
    }

    /// <summary>
    /// The results that the suite authors recorded for one test: 0 unknown, 1 passed, 2 failed, 3 crashed.
    /// </summary>
    private readonly struct SuiteResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuiteResult"/> struct.
        /// </summary>
        /// <param name="chrome">The Chrome result. 0 marks undefined behavior.</param>
        /// <param name="resvg">The resvg result.</param>
        /// <param name="librsvg">The librsvg result.</param>
        public SuiteResult(int chrome, int resvg, int librsvg)
        {
            this.Chrome = chrome;
            this.Resvg = resvg;
            this.Librsvg = librsvg;
        }

        /// <summary>
        /// Gets the Chrome result.
        /// </summary>
        public int Chrome { get; }

        /// <summary>
        /// Gets the resvg result.
        /// </summary>
        public int Resvg { get; }

        /// <summary>
        /// Gets the librsvg result.
        /// </summary>
        public int Librsvg { get; }
    }
}
