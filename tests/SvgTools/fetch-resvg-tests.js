// Copyright (c) Six Labors.
// Licensed under the Six Labors Split License.

'use strict';

// Fetches the resvg SVG conformance tests into tests/Images/External, which Git ignores.
// The sources are pinned to fixed commits, so every run gets the same files. See README.md.

const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');

const EXTERNAL = path.resolve(__dirname, '..', 'Images', 'External');

const SOURCES = {
    // The test suite repository. Its PNG files are the reference images ("how the SVG files
    // should be rendered"), 500 pixels wide for the 200 x 200 test viewBox.
    suite: {
        url: 'https://github.com/linebender/resvg-test-suite.git',
        commit: 'd8e064337faf01bc5a9579187a56dbdbe3eacc72',
        dest: path.join(EXTERNAL, 'resvg-test-suite'),
        paths: ['/tests/', '/resources/', '/fonts/', '/LICENSE', '/README.md', '/results.csv'],
        testRoot: 'tests',
    },

    // The tests inside the resvg repository (tag v0.50.0). More tests, but the PNG files are
    // resvg's own regression output, 300 pixels wide, not reference images.
    resvg: {
        url: 'https://github.com/linebender/resvg.git',
        commit: '5bdbae64eefadc4bf0caa56dcf91702037929086',
        dest: path.join(EXTERNAL, 'resvg'),
        paths: ['/crates/resvg/tests/tests/', '/crates/resvg/tests/resources/', '/crates/resvg/tests/fonts/',
            '/crates/resvg/tests/README.md', '/LICENSE-MIT', '/LICENSE-APACHE'],
        testRoot: path.join('crates', 'resvg', 'tests', 'tests'),
    },
};

const USAGE = `Usage:
  node fetch-resvg-tests.js [suite|resvg|all]

  suite  linebender/resvg-test-suite with reference PNG files (default)
  resvg  the tests in linebender/resvg v0.50.0 with resvg regression PNG files
  all    both

Files go to ${EXTERNAL}.`;

/**
 * Runs git and returns its standard output.
 * @param {string[]} args The git arguments.
 * @returns {string} The trimmed output.
 */
function git(...args) {
    return execFileSync('git', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'inherit'] }).trim();
}

/**
 * Clones one source with a sparse, blob-filtered, depth 1 fetch of the pinned commit.
 * A destination that is already at the pinned commit is left as it is.
 * @param {string} name The source name.
 * @param {object} source The source description.
 */
function fetchSource(name, source) {
    const { dest } = source;
    if (fs.existsSync(path.join(dest, '.git'))) {
        let head = '';
        try {
            head = git('-C', dest, 'rev-parse', 'HEAD');
        } catch {
            // An empty or broken clone has no HEAD. It is fetched again below.
        }

        if (head === source.commit) {
            console.log(`${name}: already at ${source.commit}`);
            return;
        }
    } else {
        fs.mkdirSync(dest, { recursive: true });
        git('init', '-q', dest);
        git('-C', dest, 'remote', 'add', 'origin', source.url);
    }

    console.log(`${name}: fetching ${source.url} at ${source.commit}`);
    git('-C', dest, 'sparse-checkout', 'set', '--no-cone', ...source.paths);
    git('-C', dest, 'fetch', '-q', '--depth', '1', '--filter=blob:none', 'origin', source.commit);
    git('-C', dest, 'checkout', '-q', '--detach', 'FETCH_HEAD');
}

/**
 * Counts the tests of one source by category and feature directory and prints the result.
 * A test is an SVG file under the test root. It has a reference image when a PNG file with
 * the same name is next to it.
 * @param {string} name The source name.
 * @param {object} source The source description.
 */
function summarize(name, source) {
    const root = path.join(source.dest, source.testRoot);
    const counts = new Map();
    let total = 0;
    let withoutPng = 0;

    for (const category of fs.readdirSync(root).sort()) {
        const categoryPath = path.join(root, category);
        if (!fs.statSync(categoryPath).isDirectory()) {
            continue;
        }

        let categoryTotal = 0;
        for (const feature of fs.readdirSync(categoryPath).sort()) {
            const featurePath = path.join(categoryPath, feature);
            if (!fs.statSync(featurePath).isDirectory()) {
                continue;
            }

            for (const file of fs.readdirSync(featurePath)) {
                if (file.endsWith('.svg')) {
                    categoryTotal++;
                    if (!fs.existsSync(path.join(featurePath, file.slice(0, -4) + '.png'))) {
                        withoutPng++;
                    }
                }
            }
        }

        counts.set(category, categoryTotal);
        total += categoryTotal;
    }

    console.log(`${name}: ${total} tests in ${root}`);
    for (const [category, count] of counts) {
        console.log(`  ${category.padEnd(16)} ${count}`);
    }

    if (withoutPng > 0) {
        console.log(`  ${withoutPng} tests have no PNG file`);
    }
}

const arg = process.argv[2] || 'suite';
const names = arg === 'all' ? Object.keys(SOURCES) : [arg];
if (names.some((n) => !(n in SOURCES))) {
    console.error(USAGE);
    process.exit(1);
}

for (const name of names) {
    fetchSource(name, SOURCES[name]);
    summarize(name, SOURCES[name]);
}
