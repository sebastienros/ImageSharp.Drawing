// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

'use strict';

// Renders SVG files to PNG files in headless Chromium through Playwright.
// The output is the independent oracle for SVG render tests. See README.md.

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const USAGE = `Usage:
  node render-svg.js [size options] <input.svg> <output.png>
  node render-svg.js [size options] --out-dir <dir> <input.svg>...

Size options (all optional):
  --width <px>          Output width. Without --height, the height keeps the aspect ratio.
  --height <px>         Output height. Without --width, the width keeps the aspect ratio.
  --long-edge <px>      Scale the intrinsic size so that its longer side is this length.
  --view-box "<x y w h>" Use this viewBox instead of the one in the file.

Font options (all optional):
  --font-dir <dir>      Render with the fonts of this directory only. Repeat for more directories.
  --generic-family <generic>=<family>
                        Map a CSS generic family (serif, sans-serif, monospace, cursive, fantasy) to a family.
  --resvg-fonts <dir>   --font-dir <dir> with the generic families of the resvg test harness.

Without size options the output has the intrinsic size of the document.`;

/**
 * Parses the command line.
 * @param {string[]} args The arguments after the script name.
 * @returns {{options: object, jobs: {input: string, output: string}[]}} The options and the files to render.
 */
function parseArgs(args) {
    const options = { width: 0, height: 0, longEdge: 0, viewBox: null, fontDirs: [], genericFamilies: {} };
    const files = [];
    let outDir = null;

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];
        const next = () => {
            if (i + 1 >= args.length) {
                fail(`${arg} needs a value`);
            }

            return args[++i];
        };

        switch (arg) {
            case '--width': options.width = positiveInt(arg, next()); break;
            case '--height': options.height = positiveInt(arg, next()); break;
            case '--long-edge': options.longEdge = positiveInt(arg, next()); break;
            case '--view-box': options.viewBox = next(); break;
            case '--font-dir': options.fontDirs.push(next()); break;
            case '--generic-family': {
                const value = next();
                const separator = value.indexOf('=');
                if (separator <= 0 || separator === value.length - 1) {
                    fail('--generic-family needs <generic>=<family>');
                }

                options.genericFamilies[value.slice(0, separator)] = value.slice(separator + 1);
                break;
            }
            case '--resvg-fonts': {
                // The fonts of the resvg suite with the generic families of the resvg test harness.
                options.fontDirs.push(next());
                Object.assign(options.genericFamilies, {
                    serif: 'Noto Serif',
                    'sans-serif': 'Noto Sans',
                    cursive: 'Yellowtail',
                    fantasy: 'Sedgwick Ave Display',
                    monospace: 'Noto Mono',
                });
                break;
            }
            case '--out-dir': outDir = next(); break;
            case '-h':
            case '--help':
                console.log(USAGE);
                process.exit(0);
                break;
            default:
                if (arg.startsWith('--')) {
                    fail(`unknown option ${arg}`);
                }

                files.push(arg);
        }
    }

    if (options.longEdge && (options.width || options.height)) {
        fail('--long-edge cannot be combined with --width or --height');
    }

    let jobs;
    if (outDir) {
        if (files.length === 0) {
            fail('no input files');
        }

        fs.mkdirSync(outDir, { recursive: true });
        jobs = files.map((input) => ({
            input,
            output: path.join(outDir, path.basename(input).replace(/\.svg$/i, '') + '.png'),
        }));
    } else {
        if (files.length !== 2) {
            fail('expected <input.svg> <output.png>, or --out-dir with input files');
        }

        jobs = [{ input: files[0], output: files[1] }];
    }

    return { options, jobs };
}

/**
 * Parses a positive integer option value.
 * @param {string} name The option name, for the error message.
 * @param {string} value The option value.
 * @returns {number} The parsed value.
 */
function positiveInt(name, value) {
    const n = Number(value);
    if (!Number.isInteger(n) || n <= 0) {
        fail(`${name} must be a positive integer`);
    }

    return n;
}

/**
 * Prints an error with the usage text and exits with code 1.
 * @param {string} message The error message.
 */
function fail(message) {
    console.error(`error: ${message}\n\n${USAGE}`);
    process.exit(1);
}

/**
 * Runs in the page. Parses the SVG, fixes the output size, and loads it into the image element.
 * The document is rendered as an image, as with an HTML <img> element: scripts do not run and
 * external resources do not load, which matches the default security model of the renderer.
 * @param {{base64: string, options: object}} input The SVG bytes and the size options.
 * @returns {Promise<object>} The output size and the intrinsic size, or an error message.
 */
async function prepareInPage({ base64, options }) {
    const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));

    // An XML document request honors the encoding declaration of the file, unlike a text decode.
    const sourceUrl = URL.createObjectURL(new Blob([bytes], { type: 'image/svg+xml' }));
    const doc = await new Promise((resolve, reject) => {
        const request = new XMLHttpRequest();
        request.open('GET', sourceUrl);
        request.responseType = 'document';
        request.overrideMimeType('image/svg+xml');
        request.onload = () => resolve(request.responseXML);
        request.onerror = () => reject(new Error('cannot read the SVG bytes'));
        request.send();
    });
    URL.revokeObjectURL(sourceUrl);

    if (!doc || doc.getElementsByTagName('parsererror').length > 0) {
        return { error: 'XML parse error' };
    }

    const root = doc.documentElement;
    if (root.localName !== 'svg' || root.namespaceURI !== 'http://www.w3.org/2000/svg') {
        return { error: 'the root element is not an SVG svg element' };
    }

    // Absolute lengths in CSS pixels. em and ex use the initial font size of 16px.
    const units = { '': 1, px: 1, in: 96, cm: 96 / 2.54, mm: 96 / 25.4, pt: 96 / 72, pc: 16, em: 16, ex: 8 };
    const length = (value) => {
        const match = /^\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*([a-z]*)\s*$/i.exec(value || '');
        if (!match || !(match[2].toLowerCase() in units)) {
            return 0;
        }

        const n = parseFloat(match[1]) * units[match[2].toLowerCase()];
        return n > 0 ? n : 0;
    };

    const parseViewBox = (value) => {
        const parts = (value || '').trim().split(/[\s,]+/).map(Number);
        return parts.length === 4 && parts.every(Number.isFinite) && parts[2] > 0 && parts[3] > 0 ? parts : null;
    };

    const viewBox = options.viewBox !== null ? parseViewBox(options.viewBox) : parseViewBox(root.getAttribute('viewBox'));
    if (options.viewBox !== null && !viewBox) {
        return { error: `invalid --view-box value "${options.viewBox}"` };
    }

    // Intrinsic size: absolute width and height; a missing side follows the viewBox aspect ratio;
    // without a viewBox the browser fallback of 300 x 150 applies.
    let w = length(root.getAttribute('width'));
    let h = length(root.getAttribute('height'));
    if (viewBox) {
        if (!w && !h) {
            w = viewBox[2];
            h = viewBox[3];
        } else if (!w) {
            w = (h * viewBox[2]) / viewBox[3];
        } else if (!h) {
            h = (w * viewBox[3]) / viewBox[2];
        }
    } else {
        w = w || 300;
        h = h || 150;
    }

    let outW;
    let outH;
    if (options.longEdge) {
        const scale = options.longEdge / Math.max(w, h);
        outW = Math.round(w * scale);
        outH = Math.round(h * scale);
    } else if (options.width && options.height) {
        outW = options.width;
        outH = options.height;
    } else if (options.width) {
        outW = options.width;
        outH = Math.round((options.width * h) / w);
    } else if (options.height) {
        outH = options.height;
        outW = Math.round((options.height * w) / h);
    } else {
        outW = Math.round(w);
        outH = Math.round(h);
    }

    outW = Math.max(1, outW);
    outH = Math.max(1, outH);

    // Map the document viewport onto the output rectangle. A document without a viewBox gets the
    // viewBox "0 0 <intrinsic width> <intrinsic height>", so it scales like one that has it.
    // preserveAspectRatio stays as written in the file.
    root.setAttribute('viewBox', (viewBox || [0, 0, w, h]).join(' '));
    root.setAttribute('width', String(outW));
    root.setAttribute('height', String(outH));

    const svgText = new XMLSerializer().serializeToString(doc);
    const image = document.getElementById('image');
    image.style.width = `${outW}px`;
    image.style.height = `${outH}px`;
    image.src = URL.createObjectURL(new Blob([svgText], { type: 'image/svg+xml' }));

    try {
        await image.decode();
    } catch (e) {
        return { error: 'Chromium could not render the SVG as an image' };
    }

    return { width: outW, height: outH, intrinsicWidth: w, intrinsicHeight: h };
}

/**
 * Renders every job with one browser instance.
 * @returns {Promise<number>} The process exit code.
 */
async function main() {
    const { options, jobs } = parseArgs(process.argv.slice(2));

    // sRGB output without a monitor color profile keeps the pixels independent of the machine. With --font-dir, Chromium
    // sees only the given fonts, through a fontconfig configuration of its own (see writeFontConfig).
    const launchOptions = { args: ['--force-color-profile=srgb'] };
    let fontConfigDir = null;
    if (options.fontDirs.length > 0) {
        fontConfigDir = writeFontConfig(options);
        launchOptions.env = { ...process.env, FONTCONFIG_FILE: path.join(fontConfigDir, 'fonts.conf') };
    }

    const browser = await chromium.launch(launchOptions);
    let exitCode = 0;
    try {
        const context = await browser.newContext({ deviceScaleFactor: 1, viewport: { width: 100, height: 100 } });
        const page = await context.newPage();

        for (const job of jobs) {
            await page.setContent(
                '<!doctype html><html><head><style>html,body{margin:0;padding:0;background:transparent;overflow:hidden}' +
                'img{display:block}</style></head><body><img id="image" alt=""></body></html>');

            const base64 = fs.readFileSync(job.input).toString('base64');
            const result = await page.evaluate(prepareInPage, { base64, options });
            if (result.error) {
                console.error(`${job.input}: ${result.error}`);
                exitCode = 2;
                continue;
            }

            await page.setViewportSize({ width: result.width, height: result.height });
            await page.screenshot({
                path: job.output,
                omitBackground: true,
                clip: { x: 0, y: 0, width: result.width, height: result.height },
            });

            console.log(`${job.input} -> ${job.output} (${result.width}x${result.height}, intrinsic ` +
                `${+result.intrinsicWidth.toFixed(3)}x${+result.intrinsicHeight.toFixed(3)})`);
        }
    } finally {
        await browser.close();
        if (fontConfigDir) {
            fs.rmSync(fontConfigDir, { recursive: true, force: true });
        }
    }

    return exitCode;
}

/**
 * Writes a fontconfig configuration that offers Chromium only the fonts of the --font-dir directories, maps the CSS
 * generic families as --generic-family says, and maps every other family name that no font has to the serif family,
 * which is also what Chromium's default font (Times New Roman) becomes.
 * @param {object} options The parsed options.
 * @returns {string} The temporary directory that holds fonts.conf and the font cache.
 */
function writeFontConfig(options) {
    const dir = fs.mkdtempSync(path.join(require('os').tmpdir(), 'render-svg-fonts-'));
    const escape = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const lines = ['<?xml version="1.0"?>', '<!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">', '<fontconfig>'];
    for (const fontDir of options.fontDirs) {
        lines.push(`  <dir>${escape(path.resolve(fontDir))}</dir>`);
    }

    lines.push(`  <cachedir>${escape(path.join(dir, 'cache'))}</cachedir>`);
    const generic = { serif: null, 'sans-serif': null, monospace: null, cursive: null, fantasy: null, ...options.genericFamilies };
    for (const [name, family] of Object.entries(generic)) {
        if (family) {
            lines.push(`  <alias binding="same"><family>${escape(name)}</family><prefer><family>${escape(family)}</family></prefer></alias>`);
        }
    }

    if (generic.serif) {
        lines.push(`  <alias binding="same"><family>Times New Roman</family><prefer><family>${escape(generic.serif)}</family></prefer></alias>`);
        lines.push('  <match target="pattern"><edit name="family" mode="append_last"><string>serif</string></edit></match>');
    }

    lines.push('</fontconfig>', '');
    fs.writeFileSync(path.join(dir, 'fonts.conf'), lines.join('\n'));
    return dir;
}

main().then(
    (code) => process.exit(code),
    (error) => {
        console.error(error);
        process.exit(2);
    });
