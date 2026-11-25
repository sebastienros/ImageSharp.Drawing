// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

'use strict';

// Compares two PNG files and prints difference metrics. See README.md.

const { readPng, writePng } = require('./png');

const USAGE = `Usage:
  node compare-png.js [options] <expected.png> <actual.png>

Options:
  --pixel-threshold <n>  Per-pixel Manhattan threshold in 8-bit units (default 0). A pixel counts
                         as different, and adds to the total difference, only above it.
  --max-difference <p>   Exit with code 1 when the total difference is above <p> percent.
  --diff <out.png>       Write a difference image: red where pixels differ, gray elsewhere.`;

/**
 * Prints an error with the usage text and exits with code 3.
 * @param {string} message The error message.
 */
function fail(message) {
    console.error(`error: ${message}\n\n${USAGE}`);
    process.exit(3);
}

/**
 * Parses the command line.
 * @param {string[]} args The arguments after the script name.
 * @returns {object} The parsed options and the two file paths.
 */
function parseArgs(args) {
    const result = { pixelThreshold: 0, maxDifference: null, diff: null, files: [] };
    for (let i = 0; i < args.length; i++) {
        const arg = args[i];
        const value = () => {
            if (i + 1 >= args.length) {
                fail(`${arg} needs a value`);
            }

            return args[++i];
        };

        switch (arg) {
            case '--pixel-threshold': result.pixelThreshold = Number(value()); break;
            case '--max-difference': result.maxDifference = Number(value()); break;
            case '--diff': result.diff = value(); break;
            case '-h':
            case '--help':
                console.log(USAGE);
                process.exit(0);
                break;
            default:
                if (arg.startsWith('--')) {
                    fail(`unknown option ${arg}`);
                }

                result.files.push(arg);
        }
    }

    if (result.files.length !== 2) {
        fail('expected two PNG files');
    }

    if (!(result.pixelThreshold >= 0) || (result.maxDifference !== null && !(result.maxDifference >= 0))) {
        fail('thresholds must be non-negative numbers');
    }

    return result;
}

const options = parseArgs(process.argv.slice(2));
const expected = readPng(options.files[0]);
const actual = readPng(options.files[1]);

if (expected.width !== actual.width || expected.height !== actual.height) {
    console.log(`size mismatch: expected ${expected.width}x${expected.height}, actual ${actual.width}x${actual.height}`);
    process.exit(2);
}

const pixelCount = expected.width * expected.height;
const a = expected.data;
const b = actual.data;
const diffImage = options.diff ? Buffer.alloc(pixelCount * 4) : null;

let differentPixels = 0;
let totalDifference = 0;
let maxChannel = 0;
let maxPremultiplied = 0;
const channelSums = [0, 0, 0, 0];

for (let p = 0; p < pixelCount; p++) {
    const o = p * 4;
    let manhattan = 0;
    for (let c = 0; c < 4; c++) {
        const d = Math.abs(a[o + c] - b[o + c]);
        channelSums[c] += d;
        manhattan += d;
        if (d > maxChannel) {
            maxChannel = d;
        }
    }

    // Straight alpha makes the color of a nearly transparent pixel arbitrary. The premultiplied
    // difference shows the visible error: color times alpha, in 8-bit units.
    for (let c = 0; c < 3; c++) {
        const d = Math.abs((a[o + c] * a[o + 3]) - (b[o + c] * b[o + 3])) / 255;
        if (d > maxPremultiplied) {
            maxPremultiplied = d;
        }
    }

    const counted = manhattan > options.pixelThreshold;
    if (counted) {
        differentPixels++;
        totalDifference += manhattan;
    }

    if (diffImage) {
        // Red marks a counted difference. Elsewhere a light gray copy of the expected luminance.
        if (counted) {
            diffImage[o] = 255;
            diffImage[o + 1] = 0;
            diffImage[o + 2] = 0;
        } else {
            const luma = Math.round((((0.299 * a[o]) + (0.587 * a[o + 1]) + (0.114 * a[o + 2])) * a[o + 3]) / 255);
            const gray = 255 - Math.round((255 - luma) * 0.3);
            diffImage[o] = gray;
            diffImage[o + 1] = gray;
            diffImage[o + 2] = gray;
        }

        diffImage[o + 3] = 255;
    }
}

// The same normalization as TolerantImageComparer in the test project: the sum of the counted
// per-pixel Manhattan distances divided by (pixel count * 4 channels * channel maximum).
// The ratio does not depend on the bit depth, so the 8-bit sums give the same value as Rgba64.
const normalized = totalDifference / (pixelCount * 4 * 255);
const percent = normalized * 100;

const fixed = (n) => n.toFixed(4);
console.log(`size                      ${expected.width}x${expected.height}`);
console.log(`different pixels          ${differentPixels} (${fixed((differentPixels * 100) / pixelCount)}%), per-pixel threshold ${options.pixelThreshold}`);
console.log(`total difference          ${fixed(percent)}% (TolerantImageComparer image threshold ${normalized.toExponential(4)})`);
console.log(`max channel difference    ${maxChannel} (straight alpha), ${Math.round(maxPremultiplied)} (premultiplied)`);
console.log(`mean channel difference   R ${fixed(channelSums[0] / pixelCount)}  G ${fixed(channelSums[1] / pixelCount)}  B ${fixed(channelSums[2] / pixelCount)}  A ${fixed(channelSums[3] / pixelCount)}`);

if (diffImage) {
    writePng(options.diff, expected.width, expected.height, diffImage);
    console.log(`difference image          ${options.diff}`);
}

if (options.maxDifference !== null && percent > options.maxDifference) {
    console.log(`FAIL: total difference ${fixed(percent)}% is above ${options.maxDifference}%`);
    process.exit(1);
}
