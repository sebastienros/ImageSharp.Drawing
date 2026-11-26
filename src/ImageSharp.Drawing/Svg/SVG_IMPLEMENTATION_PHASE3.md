# SVG Rendering: Implementation Plan (Phase 3, Text)

This document continues `SVG_IMPLEMENTATION.md` and `SVG_IMPLEMENTATION_PHASE2.md`. Read `SVG_DESIGN.md`, both earlier plans, and the repository `AGENTS.md` first. All their rules still apply: no `record` types, documentation on every member, simple technical English, Release builds, zero warnings in `src/`, no new package reference in `src/`, no existing reference image or expected result edited to silence a failure, no issue, no pull request, no push.

Where this plan says "verify", the statement comes from reading source or from memory. Check it against the code or with a test before relying on it. When the code contradicts the plan, follow the code and record the difference in the hand-back report.

When a rendering choice is open, follow what Chromium does, then what Skia and librsvg do. This is a decision of the user.

Phase 3 scope (`SVG_DESIGN.md` section 11): `text`, `tspan`, `textPath`, anchors, font matching and fallback. This plan also covers `tref`, text decoration, baselines, `textLength`, vertical writing, and bidirectional text, because the conformance suite tests them and they share one layout pipeline.

## 1. Work Packages

| Package | Content | Depends on |
|---|---|---|
| T1 | Text content in the tree, all text attributes and properties, font options and font matching, the layout pipeline for horizontal text, rendering as glyph outlines, the conformance setup for fonts. | Phase 2 |
| T2 | `textPath`, `textLength`, `lengthAdjust`. | T1 |
| T3 | `text-decoration`, baselines (`dominant-baseline`, `alignment-baseline`, `baseline-shift`), font features (`font-variant`, `font-kerning`, `kerning`). | T1 |
| T4 | Vertical `writing-mode`, `direction`, `unicode-bidi`, complex scripts, `tref`, `text-rendering`. | T1 |
| T5 | Integration: cross-feature tests, conformance totals, benchmarks with text, hostile documents, document status. | T2, T3, T4 |
| T6 | The Unicode bidirectional algorithm as a local copy from SixLabors.Fonts, replacing the T4 workaround (section 9). | T5 |

T2, T3, and T4 run at the same time in separate work trees. T1 therefore builds the pipeline as separate stages in separate files (section 4), parses everything all packages need, and leaves a documented hook for each later feature. T2, T3, and T4 each put their feature in new files and keep edits to shared files to short hooks, with new enum members and fields at the end. In the conformance expected-results list, a package changes only the lines of its own features.

## 2. Public API Additions (Package T1)

Text needs fonts, and fonts differ per machine. The options make the choice explicit:

- the font collection to search (default: the system fonts)
- the family for each generic name: `serif`, `sans-serif`, `monospace`, `cursive`, `fantasy`
- the default family, used when the document names no family or none of the named families exists
- the families to try, in order, for a character that the chosen font does not have

Design the members in the style of the existing `SvgLoadOptions` and of the SixLabors.Fonts API (verify `IReadOnlyFontCollection`, `FontCollection`, `SystemFonts`, `TextOptions.FallbackFontFamilies`). Requirements:

- Fonts load from the collection only. The document never causes a font file to be read from a path or a URL. `@font-face` rules are ignored.
- The defaults follow browsers: `serif` is the default family. When a configured family is not in the collection, fall back in a fixed, documented order, so that the result depends only on the collection.
- A document whose text finds no font at all draws no text and does not fail.
- `SvgDocument` stays immutable, thread-safe, and not disposable.

Add a limit to `SvgLimits` for the total number of characters laid out per document, counted through `use` instances.

## 3. Parsing (Package T1)

### 3.1 Text content

Phase 1 reads with `IgnoreWhitespace` on and stores elements only. Text needs character data in document order between child elements.

- Keep character data only inside `text` and its descendants, so other documents do not pay for it. Measure `LoadSvg` for the phase 1 documents before and after; they must not load slower or allocate more beyond noise.
- White space: `xml:space="default"` and `"preserve"` as SVG 1.1 defines them, and the CSS `white-space` property. Match Chromium where the two disagree.
- Entities and CDATA sections count as text. Limit the size through `SvgLimits`.
- Package S reads the input a second time for `:empty`, because the first read drops white space. Keep `:empty` correct.

### 3.2 Attributes

| Element | Attributes |
|---|---|
| `text`, `tspan` | `x`, `y`, `dx`, `dy`, `rotate` (each a list), `textLength`, `lengthAdjust` |
| `textPath` | `href`, `xlink:href`, `startOffset`, `method`, `spacing`, `side`, `path` |
| `tref` | `href`, `xlink:href` |

### 3.3 Properties

Add each with the correct inheritance and initial value: `font-family` (a list, with generic names), `font-style`, `font-weight` (keywords, numbers, `bolder`, `lighter`), `font-stretch`, `font-variant`, the `font` shorthand, `font-size-adjust`, `font-kerning`, `text-anchor`, `text-decoration` (with the `-line`, `-style`, `-color` longhands), `letter-spacing`, `word-spacing`, `dominant-baseline`, `alignment-baseline`, `baseline-shift`, `writing-mode` (SVG 1.1 and CSS values), `direction`, `unicode-bidi`, `white-space`, `text-rendering`, and the `kerning` attribute. Phase 1 has `font-size`; add its keywords and `larger` / `smaller` if they are missing.

T1 parses all of them and exposes them in the computed style. T2, T3, and T4 only read them.

## 4. Layout Pipeline (Package T1 builds it; T2, T3, T4 extend it)

SVG text layout is not paragraph layout. It works on one `text` element at a time:

1. **Characters.** Flatten the `text` subtree into one character sequence after white-space processing. Each character keeps its source element, its computed style, and its entries from the `x`, `y`, `dx`, `dy`, `rotate` lists. The lists of an ancestor apply to characters of its descendants by position; a descendant's own list wins.
2. **Fonts.** Resolve a font for each run of equal style with the CSS font matching rules: family list in order, then stretch, style, and weight matching. Split runs where the font lacks a character and use the fallback families.
3. **Shaping.** Shape each run with SixLabors.Fonts to get glyphs, advances, offsets, and the mapping from glyphs to characters (verify which API gives this: the text measuring and layout types, or the glyph renderer callbacks). Ligatures form only where no character inside the cluster has its own position.
4. **Positions.** Walk the clusters: apply absolute `x` and `y` (each starts a new text chunk), `dx` and `dy`, `letter-spacing`, `word-spacing`, and `rotate`. T3 adds baseline shifts here. T4 adds vertical advance and bidi reordering here. T2 adds `textLength` here.
5. **Anchors.** Shift each text chunk for `text-anchor`, using the chunk's advance.
6. **Paths.** T2 maps chunks on a `textPath` to the path here.
7. **Output.** Emit each glyph as its outline under its position and rotation, as ordinary draw nodes, so fill, stroke, paint servers, `paint-order`, opacity, clips, and masks work as for shapes. Decorations (T3) are rectangles in the same order as browsers paint them.

Decisions:

- **Glyph outlines at load.** The document resolves text into paths when it loads, as resvg does. All existing machinery then applies, on both backends. Share one outline per font and glyph in a cache for the load, and place instances with transforms; do not copy geometry per glyph instance. Verify how `TextBuilder` and the glyph builders in `src/ImageSharp.Drawing/Text/` produce outlines and whether they can be reused. Colour glyphs (COLR, emoji) render with their layers if the existing glyph builders provide them; otherwise list them as a gap.
- **Bounding box.** The object bounding box of a `text` element is the union of its glyph cells (advance by ascent and descent), not the ink bounds. Paint servers in bounding-box units on text use the box of the whole `text` element, also for a `tspan`.
- **Text in `clipPath`.** `text` children add their glyph outlines to the clip. Phase 2 left this out; T1 adds it.
- **Paint on `tspan`.** Each `tspan` can set fill, stroke, and opacity. Group consecutive glyphs of equal paint into one draw node where the order of painting allows it.
- **Each stage in its own file** (`Text/` folder under `Svg/`), with internal contracts documented, because three packages extend the stages in parallel.

## 5. Packages T2, T3, T4

### 5.1 T2: `textPath`, `textLength`

- `textPath` places each cluster at the path point of the middle of its advance and rotates it to the path direction. Clusters whose middle is before the start or after the end of the path do not render. `startOffset` takes a length or a percentage of the path length. The referenced element is a `path`, or any basic shape (SVG 2); the `path` attribute too if Chromium supports it. The transform of the referenced path applies.
- `x`, `dx` move along the path; `y`, `dy` move perpendicular to it. `text-anchor` shifts along the path. A closed path wraps as Chromium does. `side`, `method`, and `spacing`: match Chromium.
- Text after a `textPath` continues at the end point of the path.
- `textLength` with `lengthAdjust="spacing"` adjusts the advances; `"spacingAndGlyphs"` also scales the glyphs along the inline direction. It applies per element that sets it, including nested `tspan` elements and text on a path.
- Path length and point-at-length: verify what `IPath` and its internals offer and reuse the code that package F wrote for marker directions where it fits.

### 5.2 T3: decoration, baselines, font features

- `text-decoration`: underline, overline, and line-through, with position and thickness from the font metrics. The paint of a decoration comes from the element that set the decoration, not from the element of the text; decorations of nested elements paint in the order Chromium uses. `text-decoration-style` other than `solid` and `text-decoration-color`: match Chromium if cheap.
- `dominant-baseline` and `alignment-baseline`: use the font baseline tables where the font has them and the CSS fallback rules otherwise. `baseline-shift`: `sub`, `super`, lengths, and percentages, cumulative through nested elements.
- `font-variant: small-caps`, `font-kerning`, and the `kerning` attribute through shaping features (verify what SixLabors.Fonts exposes).

### 5.3 T4: vertical text, bidi, scripts, `tref`

- `writing-mode` vertical: glyphs advance downward, upright or rotated by script as Chromium does, with vertical metrics where the font has them. `glyph-orientation-vertical` is deprecated; support `0` and `90` if Chromium does.
- `direction` and `unicode-bidi`: reorder with the Unicode bidirectional algorithm (verify what SixLabors.Fonts provides). `text-anchor` follows the direction. Position lists apply in logical order.
- Complex scripts (Arabic, Devanagari): shaping comes from SixLabors.Fonts; check the suite's tests and report the differences.
- `tref`: removed from SVG 2 and from Chromium; resvg supports it. Follow Chromium (no rendering) unless the cost of supporting it is small; report the choice and the test counts either way.
- `text-rendering`: map to antialiasing or hinting only if the canvas offers a matching control.

## 6. Conformance and Evidence

- **Fonts of the suite.** The resvg suite ships its fonts in `fonts/` with their licenses. The runner loads them into a font collection and sets the generic families as the suite expects (read the suite documentation and the resvg test harness notes for the mapping; verify, do not guess). Do not copy the suite's fonts into the repository.
- **Oracle with the same fonts.** Chromium must render with the same font files, or the comparison means nothing. Make the suite's fonts available to the oracle (a per-user font directory in the container, or `@font-face` injection in `tests/SvgTools/render-svg.js`) and document how.
- **Calibration.** The phase 1 calibration of the pass criterion left out the text category. With the fonts in place, measure how many text tests the Chromium render passes, and report it. Do not change the pass criterion. Shaping and hinting differ between engines, so some text tests will fail for small position differences; give those a specific reason.
- **Repository tests.** Feature tests use fonts that are already in `tests/ImageSharp.Drawing.Tests/TestFonts/`. Each new reference image needs an oracle comparison with the same font, as before.
- Every `text` test in the list that still fails gets a specific reason. Phase 1 and phase 2 passes must not regress.

## 7. Package T5: Integration

- Cross-feature tests: text with gradients and patterns in both unit types, text in `clipPath` and `mask`, text in `pattern` and `marker` content, `use` of text, text under `switch` with `systemLanguage`, CSS selectors that set font properties.
- Conformance totals per category next to the earlier counts.
- Benchmarks: documents with text against Svg.Skia and NetVips with the same font files available to every renderer (fairness rules: `SVG_DESIGN.md` section 9.3). Check output comparability first. Report load cost separately, because text resolves at load.
- Hostile documents: very long text, very many `tspan` elements, huge position lists, a `use` storm of text, text on a very long path, fonts with very many glyphs. Each stays within `SvgLimits` or finishes in bounded time.
- Update `SVG_DESIGN.md`: status, feature table, known gaps, measured results.

## 8. Hand-back Report

As in `SVG_IMPLEMENTATION.md` section 9. Do not commit and do not push; the main session reviews, commits, and merges.

## 9. Package T6: The Bidirectional Algorithm (added after T5)

T4 could not use the Unicode bidirectional algorithm, because it is internal in SixLabors.Fonts 3.2.1. It gets the visual order from one extra call of the public `TextShaper.Shape` per chunk and passes `direction` and `unicode-bidi` as control characters. A lone bracket between runs of the other direction is not mirrored.

Decision of the user: copy the algorithm into this repository, with a comment that names the source, until SixLabors.Fonts makes it public. The user files the upstream issue; this package only drafts its text.

### 9.1 The copy

- Source: SixLabors.Fonts at tag `v3.2.1` (commit `50dae68`), the version this repository references. A clone at that tag is at `/home/user/sixlabors/fonts`. The files are in `src/SixLabors.Fonts/Unicode/`: `BidiAlgorithm.cs`, `BidiData.cs`, `BidiData.AsciiTables.Generated.cs`, `BidiRun.cs`, `BidiDictionary{T1,T2}.cs`, and the internal helper types they need (`ArraySlice<T>`, `ReadOnlyArraySlice<T>`, `ArrayBuilder<T>`, `MappedArraySlice<T>` in `src/SixLabors.Fonts/`).
- Target: `src/ImageSharp.Drawing/Svg/Text/Bidi/`, all types internal, in the namespace of the other SVG types.
- Copy only what the algorithm needs. Unicode data comes from the public API of SixLabors.Fonts (`CodePoint.GetBidiClass`, `BidiClass`, `BidiCharacterType`, `BidiPairedBracketType`, `CodePoint.TryGetBidiMirror`); do not copy data tries.
- Keep the copied code as close to the source as it compiles, so that it can be compared with upstream and removed later. Allowed edits: namespace, visibility, replacing a helper by a span or an array where that removes a helper file, documentation that the analyzers of this repository require, and warnings. List every other edit in the hand-back report.
- Each copied file starts its type remarks with: the source repository, the tag, the file path, a statement that it is a temporary copy until SixLabors.Fonts exposes the algorithm, and that it must then be removed.
- Licenses: SixLabors.Fonts has the same owner and license as this repository. Its `src/SixLabors.Fonts/Unicode/README.md` credits RichTextKit (Apache 2.0) among the sources of its Unicode code. Check the `THIRD-PARTY-NOTICES` of SixLabors.Fonts and carry over the entries that apply to the copied files into the `THIRD-PARTY-NOTICES` of this repository.

### 9.2 Use

- Compute the embedding levels per text chunk (one chunk is one paragraph, as now) with the paragraph level from `direction`. Map `unicode-bidi` (`embed`, `bidi-override`, `isolate`, `isolate-override`, `plaintext`) as CSS Writing Modes defines, with the control characters or directly through the algorithm.
- Derive the level runs and the visual order (rule L2) from the levels, shape each run in its direction with `TextShaper.ShapeRun`, and mirror characters at odd levels (rule L4). Verify whether the shaper already mirrors in a right-to-left run; do not mirror twice.
- Remove the extra `TextShaper.Shape` call of the T4 workaround when nothing else needs it. Measure `LoadSvg` for the text documents before and after.
- The two other known gaps stay, because they are limits of the shaping API and not of the bidirectional algorithm: Arabic letters do not join across a `tspan` boundary, and `letter-spacing` cannot turn ligatures off. Confirm that with a test each and say so in the report.

### 9.3 Evidence

- Unit tests of the visual order for the examples of UAX #9 and for brackets, isolates, overrides, and numbers in right-to-left text. Do not copy the Unicode conformance data files.
- A feature document with mixed runs and brackets, with a reference image compared with the Chromium oracle and the same font.
- The render tests of T4 and T5 stay green. Conformance counts for `text/direction`, `text/unicode-bidi`, and the whole text category before and after.
- Hostile input: long runs of embedding and isolate controls, deep nesting beyond the depth limit of the algorithm, unmatched controls.

### 9.4 Upstream issue draft

Put a draft of the issue for SixLabors.Fonts in the hand-back report, and do not post it: what is internal today, the smallest public API that would remove the copy (levels for a span of text with a paragraph direction, and the level runs), and why run-level shaping needs it. Do not name this project's private details beyond "SVG text layout on top of `TextShaper.ShapeRun`". Mention the two shaping gaps of 9.2 as separate, optional requests.
