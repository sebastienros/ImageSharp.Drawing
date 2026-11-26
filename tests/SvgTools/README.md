# SVG Developer Tools

Tools for SVG rendering work. They are not part of `dotnet test` and not a build dependency.

| Tool | Purpose |
|---|---|
| `render-svg.js` | Renders SVG files to PNG files in headless Chromium. This is the independent oracle for new reference images. |
| `compare-png.js` | Compares two PNG files and prints difference metrics. |
| `fetch-resvg-tests.js` | Fetches the resvg conformance tests into `tests/Images/External`, which Git ignores. |

Requirements: Node.js 22.2 or later and git. `render-svg.js` also needs the `playwright` package and its Chromium build. The development container has both. On another machine, install them with `npm install -g playwright` and `npx playwright install chromium`, and make the global package visible to `require` (for example through `NODE_PATH`). The other two tools use Node built-ins only.

Run the commands from the repository root.

## Render oracle

```
node tests/SvgTools/render-svg.js [size options] <input.svg> <output.png>
node tests/SvgTools/render-svg.js [size options] --out-dir <dir> <input.svg>...
```

| Option | Effect |
|---|---|
| `--width <px>` | Output width. Without `--height`, the height keeps the aspect ratio. |
| `--height <px>` | Output height. Without `--width`, the width keeps the aspect ratio. |
| `--long-edge <px>` | Scales the intrinsic size so that its longer side has this length. |
| `--view-box "<x y w h>"` | Uses this viewBox instead of the one in the file. |

Without size options the output has the intrinsic size of the document.

| Font option | Effect |
|---|---|
| `--font-dir <dir>` | Chromium sees only the fonts of this directory. Repeat the option for more directories. |
| `--generic-family <generic>=<family>` | Maps a CSS generic family (`serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`) to a family of the font directories. |
| `--resvg-fonts <dir>` | `--font-dir <dir>` with the generic families of the resvg test harness: serif Noto Serif, sans-serif Noto Sans, cursive Yellowtail, fantasy Sedgwick Ave Display, monospace Noto Mono. |

Without font options Chromium uses the fonts of the machine. A text render compares only when Chromium and the renderer use the same font files, so every oracle render of text uses the font options.

Examples:

```
node tests/SvgTools/render-svg.js --long-edge 1000 tests/Images/Input/Svg/Ghostscript_Tiger.svg tiger.png
node tests/SvgTools/render-svg.js --width 500 --out-dir out tests/Images/External/resvg-test-suite/tests/shapes/rect/*.svg
```

How it renders:

- The output is the document drawn into a rectangle of the output size, as `canvas.DrawSvg(document, new RectangleF(0, 0, width, height))` does. The viewBox maps to the rectangle with the `preserveAspectRatio` of the file. A document without a viewBox gets `0 0 <intrinsic width> <intrinsic height>`, so it scales the same way.
- Intrinsic size: absolute `width` and `height`; a missing or percentage side follows the viewBox aspect ratio; without a viewBox the fallback is 300 x 150. `em` is 16px and `ex` is 8px. These are the rules of `SVG_IMPLEMENTATION.md` section 5.2. Only the size of the rectangle comes from these rules. Chromium does all of the drawing.
- Chromium renders the file as an image, as an HTML `<img>` element does: scripts do not run and external files do not load. This matches the default security model of the renderer. Tests that need external images, such as `structure/image` in the resvg suite, do not render them.
- Device scale factor 1, transparent background, sRGB color profile. The PNG has straight alpha.
- With font options the script writes a temporary fontconfig configuration and starts Chromium with `FONTCONFIG_FILE` pointing to it. The configuration lists only the font directories, maps the generic families, and maps Times New Roman (the default font of Chromium) and every family that no font has to the serif family. The configuration and its font cache are removed when the script ends. This works on Linux, where Chromium finds fonts through fontconfig.
- Text references: the repository tests (`SvgTextRenderTests`) use Open Sans from `tests/ImageSharp.Drawing.Tests/TestFonts`. Render them with `--font-dir <dir with OpenSans-Regular.ttf> --generic-family "serif=Open Sans"`. The resvg conformance tests use `--resvg-fonts tests/Images/External/resvg-test-suite/fonts`.
- `paris-30k.svg` has no size and no viewBox. To render the whole map, pass `--view-box "0 0 1096 1060" --width 1096`, or render the copy that `--verify svg` writes (see below), which has a viewport.

Before you accept a new reference image for a render test, render the same SVG with this tool at the same size and compare the two files. A difference means a defect in the renderer or a documented gap. It is not a reason to raise the tolerance.

## PNG comparison

```
node tests/SvgTools/compare-png.js [options] <expected.png> <actual.png>
```

| Option | Effect |
|---|---|
| `--pixel-threshold <n>` | Per-pixel Manhattan threshold in 8-bit units (default 0). A pixel counts as different, and adds to the total difference, only above it. |
| `--max-difference <p>` | Exits with code 1 when the total difference is above `p` percent. |
| `--diff <out.png>` | Writes a difference image: red where pixels differ, gray elsewhere. |

Output:

- `different pixels`: the pixels whose Manhattan distance (sum of the R, G, B, A differences) is above the threshold.
- `total difference`: the metric of `TolerantImageComparer` in the test project. The percent value is the argument of `ImageComparer.TolerantPercentage(...)`. A per-pixel threshold of `n` here equals `perPixelManhattanThreshold` `n * 257` there, because the test comparer works in 16-bit units.
- `max channel difference`: in straight alpha, and premultiplied (color times alpha), which ignores the color of transparent pixels.
- `mean channel difference`: the mean absolute difference per channel.

Exit codes: 0 compared, 1 above `--max-difference`, 2 size mismatch, 3 usage error. Interlaced PNG files are not supported.

## resvg conformance tests

```
node tests/SvgTools/fetch-resvg-tests.js [suite|resvg|all]
```

The script clones a pinned commit with a sparse, depth 1, blob-filtered fetch, then prints the number of tests per category. A second run with the same commit does nothing. The files are not committed.

| Source | Repository and commit | Destination | Tests | PNG files |
|---|---|---|---|---|
| `suite` (default) | `linebender/resvg-test-suite` at `d8e064337faf01bc5a9579187a56dbdbe3eacc72` (2024-10-29, the newest commit) | `tests/Images/External/resvg-test-suite` | 1679 | Reference images, 500 px wide |
| `resvg` | `linebender/resvg` tag `v0.50.0`, `5bdbae64eefadc4bf0caa56dcf91702037929086` | `tests/Images/External/resvg` | 1780 | resvg regression output, 300 px wide |

The resvg documentation (`crates/resvg/tests/README.md`) names the test suite repository as the source of truth: its PNG files show how the SVG files should render. The PNG files in the resvg repository are what resvg itself renders, kept for regression tests. Of the 1675 tests in both sources, 1661 SVG files are identical and only 4 PNG files are identical, because the sizes differ. The resvg repository has 105 newer tests (mostly text, patterns, images, and `structure/svg`).

Layout of `resvg-test-suite`:

```
tests/<category>/<feature>/<name>.svg    the test
tests/<category>/<feature>/<name>.png    the reference image, next to the test
resources/                               files that tests reference (images, CSS, nested SVG)
fonts/                                   fonts that text tests use, with their licenses
results.csv                              manual results per test and renderer
LICENSE                                  MIT
```

Every test has a PNG file. 1624 tests have `viewBox="0 0 200 200"`; the other 55 test sizing itself. 1657 reference images are 500 x 500, 20 are 500 x 250, and 2 are 400 x 400; render at the PNG size. The layout of `tests/Images/External/resvg/crates/resvg/tests/tests` is the same.

`results.csv` has one row per test (`title` is the path under `tests/`) and one column per renderer: chrome, firefox, safari, resvg, batik, inkscape, librsvg, svgnet, qtsvg. Values: 0 unknown, 1 passed, 2 failed, 3 crashed. The suite statistics script skips a test when the chrome value is 0, which marks undefined behavior; 61 tests have it. Passed counts: chrome 1425, firefox 1387, safari 1343, resvg 1522, batik 978, inkscape 987, librsvg 1171, svgnet 530, qtsvg 593.

Tests per category in `resvg-test-suite`:

| Category | Tests | Features (tests) |
|---|---|---|
| filters | 397 | enable-background 21, feBlend 10, feColorMatrix 16, feComponentTransfer 22, feComposite 18, feConvolveMatrix 25, feDiffuseLighting 22, feDisplacementMap 1, feDistantLight 4, feDropShadow 8, feFlood 8, feGaussianBlur 13, feImage 27, feMerge 3, feMorphology 14, feOffset 9, fePointLight 4, feSpecularLighting 8, feSpotLight 12, feTile 7, feTurbulence 19, filter 74, filter-functions 43, flood-color 7, flood-opacity 2 |
| masking | 93 | clip 1, clip-rule 1, clipPath 52, mask 39 |
| paint-servers | 149 | linearGradient 38, pattern 31, radialGradient 45, stop 32, stop-color 1, stop-opacity 2 |
| painting | 304 | color 4, context 15, display 9, fill 60, fill-opacity 8, fill-rule 2, image-rendering 3, isolation 2, marker 63, mix-blend-mode 20, opacity 9, overflow 5, paint-order 14, shape-rendering 8, stroke 20, stroke-dasharray 17, stroke-dashoffset 6, stroke-linecap 9, stroke-linejoin 5, stroke-miterlimit 5, stroke-opacity 8, stroke-width 5, visibility 7 |
| shapes | 133 | circle 6, ellipse 12, line 10, path 57, polygon 5, polyline 5, rect 38 |
| structure | 247 | a 5, defs 7, g 2, image 49, style 16, style-attribute 4, svg 42, switch 13, symbol 16, systemLanguage 10, transform 19, transform-origin 23, use 41 |
| text | 356 | 29 features, from alignment-baseline to writing-mode |

Per category in the resvg repository: filters 416, masking 100, paint-servers 163, painting 314, shapes 133, structure 267, text 387.

The conformance runner is `SvgResvgConformanceTests` in the test project. It runs every test of `resvg-test-suite` and checks the outcome against `tests/ImageSharp.Drawing.Tests/Svg/ResvgExpectedResults.txt`, which also describes the pass criterion and its calibration. Without the suite the test returns early. It writes `results.tsv` (outcome and mismatched pixels per test), `summary.txt` (counts per category), and the render of every failing test to `tests/Images/ActualOutput/Svg/Resvg`.

For information, without changing any outcome, the summary also counts the failing text tests whose mismatched pixels all lie on the 0.5 wide crosshair line that most text tests draw (the element with the id `crosshair`; the runner reads its geometry from the test document), and those with at most 40 mismatched pixels elsewhere. The header of the expected results file explains why that line fails. With the environment variable `RESVG_CHROMIUM_RENDERS` set to a directory of Chromium renders in the layout of the suite's `tests` directory, the summary gives the same counts for Chromium:

```
for d in tests/Images/External/resvg-test-suite/tests/text/*/; do
  node tests/SvgTools/render-svg.js --resvg-fonts tests/Images/External/resvg-test-suite/fonts --width 500 --height 500 \
    --out-dir <dir>/text/$(basename $d) $d*.svg
done
RESVG_CHROMIUM_RENDERS=<dir> dotnet test tests/ImageSharp.Drawing.Tests -c Release --filter FullyQualifiedName~SvgResvgConformanceTests
```

Four text tests have reference images of another size (two 500 x 250, two 400 x 400); render those at their size, or the summary leaves them out of the Chromium count.

Text in the runner uses only the fonts of the suite: the `.ttf` and `.otf` files of `fonts/`, loaded in ordinal order of the file names into one font collection, with the generic families of the resvg test harness (`crates/resvg/tests/integration/main.rs` in the resvg repository: serif Noto Serif, sans-serif Noto Sans, cursive Yellowtail, fantasy Sedgwick Ave Display, monospace Noto Mono). The fonts stay in the suite directory; they are not copied into the repository. A few reference images of the suite draw generic families with fonts that the suite does not ship (Times New Roman, Arial, Comic Sans MS, Impact); those tests fail with a reason that says so.

Licenses:

- `resvg-test-suite`: MIT, Copyright (c) 2018 Reizner Evgeniy (`LICENSE`).
- The resvg repository: Apache-2.0 or MIT, Copyright 2017 the Resvg Authors (`LICENSE-APACHE`, `LICENSE-MIT`).
- Fonts in `resvg-test-suite/fonts`, each with its license file: Amiri, M PLUS 1p, Sedgwick Ave Display, and Source Sans Pro under the SIL Open Font License 1.1; the Noto fonts (Noto Sans, Noto Serif, Noto Mono, Noto Sans Devanagari, Noto Emoji, Noto Color Emoji) under the SIL Open Font License 1.1 (`Noto-LICENSE-OFL.txt`); Yellowtail under the Apache License 2.0.
- The resvg repository adds: CFF-and-SBIX and the Noto Color Emoji CBDT subset under the Apache License 2.0; Roboto Flex, Noto Sans Malayalam, and Noto Znamenny Musical Notation under the SIL Open Font License 1.1; a Twitter Color Emoji subset with an MIT license file.

## Benchmarks

The reference renderer benchmarks are in `tests/ImageSharp.Drawing.Benchmarks/Svg`: `LoadSvg`, `RenderSvg`, and `LoadAndRenderSvg`. Build the project in Release, then run from the repository root:

```
dotnet artifacts/bin/tests/ImageSharp.Drawing.Benchmarks/Release/net10.0/ImageSharp.Drawing.Benchmarks.dll --svg-info
dotnet artifacts/bin/tests/ImageSharp.Drawing.Benchmarks/Release/net10.0/ImageSharp.Drawing.Benchmarks.dll --verify svg 1000
dotnet artifacts/bin/tests/ImageSharp.Drawing.Benchmarks/Release/net10.0/ImageSharp.Drawing.Benchmarks.dll --filter "*.Svg.*"
dotnet artifacts/bin/tests/ImageSharp.Drawing.Benchmarks/Release/net10.0/ImageSharp.Drawing.Benchmarks.dll --filter "*.Svg.*" --job short
```

- `--svg-info` prints the libvips version and configuration and renders the Tiger with both renderers, to prove that the native libraries load.
- `--verify svg <long edge>` writes, for every corpus document, the exact input bytes (`<name>.svg`) and the output of each renderer (`<name>-svgskia.png`, `<name>-netvips.png`, `<name>-imagesharp.png`) to `svg-<long edge>-verify` next to the assembly. It also checks that the ImageSharp.Drawing retained scene gives the same pixels as a fresh render. Render the `.svg` files with `render-svg.js --long-edge <long edge> --out-dir <dir>` and compare. Design section 9.3 asks for this check before benchmark numbers count.
- `FONTCONFIG_FILE=$PWD/tests/ImageSharp.Drawing.Benchmarks/Svg/fonts.conf` before the command gives NetVips (librsvg through Pango and fontconfig) the font files of the benchmarks and no other font. ImageSharp.Drawing loads the same files into its font collection and Svg.Skia gets a typeface provider for each (`SvgBenchmarkFonts`); the SkiaSharp native assets of the benchmarks are built without fontconfig. The text documents of the corpus name their family (Open Sans), so no output depends on the fonts of the machine. Without the variable, `--verify` and `--svg-info` print a warning.
- `SVG_BENCHMARK_CORPUS=<entry>,<entry>` and `SVG_BENCHMARK_SIZES=<px>,<px>` limit the corpus entries and the long-edge sizes of a run; the benchmark processes inherit them. Example: `SVG_BENCHMARK_CORPUS=TextHeavy,TextDecorations SVG_BENCHMARK_SIZES=100,1000 dotnet ... --filter "*.Svg.*" --job short`.
- `--profile svg <iterations> <corpus entry> <long edge> [single]` prints the mean time and the allocated bytes of each ImageSharp.Drawing stage: the XML read with the cascade, the resolve stage, the emit stage, the scene build, and the scene render. `single` uses one thread.
- A `--job` argument replaces the default long job of the benchmark project.

The corpus entries (`SvgBenchmarkCorpus`) are `Tiger`, `Paris30k`, `Icons` (all 40 icons in one operation), `SoftIceCream`, `Moose`, `StyledLandscape`, the two phase 2 documents `Dashboard` (clips, masks, blend modes) and `PatternChart` (patterns, markers, an embedded image), the two phase 3 documents `TextHeavy` (41 text elements, tspans, anchors, spacing, rotate lists) and `TextDecorations` (text on paths, decorations, gradients, strokes, textLength), and the three phase 4 documents `FilterShadows` (96 drop shadows), `FilterBlur` (one large blur), and `FilterChain` (chains of several primitive kinds). The benchmarks give `paris-30k.svg` a 1096 x 1060 viewport in memory, because the file has none. The long-edge sizes are 100, 1000, and 4000 pixels.

Run time: the short job runs the 78 SVG benchmarks of the six phase 1 entries in about 45 minutes on a 4-core machine; the two phase 2 entries add 26 more. `Paris30k` takes most of it: at the 4000 pixel size one Svg.Skia operation takes about a minute. The default job (3 launches, 40 warmup and 40 measured iterations) multiplies that time, so filter the run, for example with `--filter "*LoadAndRenderSvg*"`, when you do not need every case.
