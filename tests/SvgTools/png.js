// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

'use strict';

// A small PNG reader and writer for the SVG developer tools.
// It uses only Node built-ins, so the tools need no npm install.
// The reader converts every non-interlaced PNG to straight-alpha RGBA with 8 bits per channel.

const fs = require('fs');
const zlib = require('zlib');

const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

// Samples per pixel for each PNG color type.
const CHANNELS = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 };

/**
 * Reads a PNG file.
 * @param {string} path The file path.
 * @returns {{width: number, height: number, data: Buffer}} RGBA pixels, 4 bytes per pixel, row-major.
 */
function readPng(path) {
    const file = fs.readFileSync(path);
    if (file.length < 8 || !file.subarray(0, 8).equals(SIGNATURE)) {
        throw new Error(`${path}: not a PNG file`);
    }

    let width = 0;
    let height = 0;
    let bitDepth = 0;
    let colorType = 0;
    let interlace = 0;
    let palette = null;
    let transparency = null;
    const idat = [];

    let offset = 8;
    while (offset + 8 <= file.length) {
        const length = file.readUInt32BE(offset);
        const type = file.toString('latin1', offset + 4, offset + 8);
        const body = file.subarray(offset + 8, offset + 8 + length);
        offset += 12 + length;

        if (type === 'IHDR') {
            width = body.readUInt32BE(0);
            height = body.readUInt32BE(4);
            bitDepth = body[8];
            colorType = body[9];
            interlace = body[12];
        } else if (type === 'PLTE') {
            palette = body;
        } else if (type === 'tRNS') {
            transparency = body;
        } else if (type === 'IDAT') {
            idat.push(body);
        } else if (type === 'IEND') {
            break;
        }
    }

    if (interlace !== 0) {
        throw new Error(`${path}: interlaced PNG files are not supported`);
    }

    if (!(colorType in CHANNELS)) {
        throw new Error(`${path}: unknown PNG color type ${colorType}`);
    }

    const channels = CHANNELS[colorType];
    const bitsPerPixel = channels * bitDepth;
    const stride = Math.ceil((width * bitsPerPixel) / 8);

    // Filters work on bytes. For bit depths below 8 the filter distance is one byte.
    const bytesPerPixel = Math.max(1, bitsPerPixel >> 3);
    const raw = zlib.inflateSync(Buffer.concat(idat));
    const rows = Buffer.alloc(stride * height);
    unfilter(raw, rows, stride, height, bytesPerPixel);

    const data = Buffer.alloc(width * height * 4);
    const maxSample = (1 << bitDepth) - 1;

    for (let y = 0; y < height; y++) {
        const row = rows.subarray(y * stride, (y + 1) * stride);
        for (let x = 0; x < width; x++) {
            const samples = new Array(channels);
            for (let c = 0; c < channels; c++) {
                samples[c] = readSample(row, (x * channels) + c, bitDepth);
            }

            const o = ((y * width) + x) * 4;
            if (colorType === 3) {
                // Palette index. tRNS holds one alpha value per palette entry; missing entries are opaque.
                const index = samples[0];
                data[o] = palette[index * 3];
                data[o + 1] = palette[(index * 3) + 1];
                data[o + 2] = palette[(index * 3) + 2];
                data[o + 3] = transparency && index < transparency.length ? transparency[index] : 255;
                continue;
            }

            // Scale every sample to 8 bits. 16-bit samples keep the high byte after rounding.
            const to8 = (v) => Math.round((v * 255) / maxSample);
            if (colorType === 0 || colorType === 4) {
                const gray = to8(samples[0]);
                data[o] = gray;
                data[o + 1] = gray;
                data[o + 2] = gray;
                data[o + 3] = colorType === 4 ? to8(samples[1]) : 255;

                // tRNS for grayscale names one fully transparent gray level.
                if (colorType === 0 && transparency && samples[0] === transparency.readUInt16BE(0)) {
                    data[o + 3] = 0;
                }
            } else {
                data[o] = to8(samples[0]);
                data[o + 1] = to8(samples[1]);
                data[o + 2] = to8(samples[2]);
                data[o + 3] = colorType === 6 ? to8(samples[3]) : 255;

                // tRNS for truecolor names one fully transparent color.
                if (colorType === 2 && transparency &&
                    samples[0] === transparency.readUInt16BE(0) &&
                    samples[1] === transparency.readUInt16BE(2) &&
                    samples[2] === transparency.readUInt16BE(4)) {
                    data[o + 3] = 0;
                }
            }
        }
    }

    return { width, height, data };
}

/**
 * Reads sample number <c>index</c> from a row of packed samples.
 * @param {Buffer} row The unfiltered row.
 * @param {number} index The sample index in the row.
 * @param {number} bitDepth The bits per sample: 1, 2, 4, 8, or 16.
 * @returns {number} The sample value.
 */
function readSample(row, index, bitDepth) {
    if (bitDepth === 8) {
        return row[index];
    }

    if (bitDepth === 16) {
        return row.readUInt16BE(index * 2);
    }

    // Sub-byte samples are packed from the most significant bit.
    const bitOffset = index * bitDepth;
    const shift = 8 - bitDepth - (bitOffset & 7);
    return (row[bitOffset >> 3] >> shift) & ((1 << bitDepth) - 1);
}

/**
 * Reverses the PNG row filters (None, Sub, Up, Average, Paeth).
 * @param {Buffer} raw The inflated data: one filter byte, then the row bytes, for every row.
 * @param {Buffer} out The destination for the unfiltered rows.
 * @param {number} stride The row length in bytes, without the filter byte.
 * @param {number} height The row count.
 * @param {number} bpp The filter distance in bytes.
 */
function unfilter(raw, out, stride, height, bpp) {
    for (let y = 0; y < height; y++) {
        const filter = raw[y * (stride + 1)];
        const src = (y * (stride + 1)) + 1;
        const dst = y * stride;
        const prev = dst - stride;
        for (let i = 0; i < stride; i++) {
            const x = raw[src + i];
            const a = i >= bpp ? out[dst + i - bpp] : 0;
            const b = y > 0 ? out[prev + i] : 0;
            const c = i >= bpp && y > 0 ? out[prev + i - bpp] : 0;
            let value;
            switch (filter) {
                case 0: value = x; break;
                case 1: value = x + a; break;
                case 2: value = x + b; break;
                case 3: value = x + ((a + b) >> 1); break;
                case 4: {
                    const p = a + b - c;
                    const pa = Math.abs(p - a);
                    const pb = Math.abs(p - b);
                    const pc = Math.abs(p - c);
                    value = x + (pa <= pb && pa <= pc ? a : (pb <= pc ? b : c));
                    break;
                }
                default:
                    throw new Error(`unknown PNG filter type ${filter}`);
            }

            out[dst + i] = value & 0xff;
        }
    }
}

/**
 * Writes straight-alpha RGBA pixels as an 8-bit RGBA PNG file without row filters.
 * @param {string} path The file path.
 * @param {number} width The image width.
 * @param {number} height The image height.
 * @param {Buffer} data RGBA pixels, 4 bytes per pixel, row-major.
 */
function writePng(path, width, height, data) {
    const stride = width * 4;
    const raw = Buffer.alloc((stride + 1) * height);
    for (let y = 0; y < height; y++) {
        // Filter byte 0 (None) followed by the row.
        data.copy(raw, (y * (stride + 1)) + 1, y * stride, (y + 1) * stride);
    }

    const header = Buffer.alloc(13);
    header.writeUInt32BE(width, 0);
    header.writeUInt32BE(height, 4);
    header[8] = 8;
    header[9] = 6;

    fs.writeFileSync(path, Buffer.concat([
        SIGNATURE,
        chunk('IHDR', header),
        chunk('IDAT', zlib.deflateSync(raw)),
        chunk('IEND', Buffer.alloc(0)),
    ]));
}

/**
 * Builds one PNG chunk: length, type, body, and the CRC of type and body.
 * @param {string} type The four-letter chunk type.
 * @param {Buffer} body The chunk data.
 * @returns {Buffer} The encoded chunk.
 */
function chunk(type, body) {
    const typeBytes = Buffer.from(type, 'latin1');
    const length = Buffer.alloc(4);
    length.writeUInt32BE(body.length, 0);
    const crc = Buffer.alloc(4);
    crc.writeUInt32BE(zlib.crc32(Buffer.concat([typeBytes, body])) >>> 0, 0);
    return Buffer.concat([length, typeBytes, body, crc]);
}

module.exports = { readPng, writePng };
