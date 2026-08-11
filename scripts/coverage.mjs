// Reads the Cobertura report scripts/coverage.sh just produced and answers one question: is the
// code this branch *changed* covered? See docs/testing.md.
//
// The distinction is the whole point. Core sits above 96% line coverage, so a solution-wide gate at
// 80% passes with a brand-new untested service in the diff — the number moves by tenths. Judging
// the added lines instead makes the gate about the change, which is the only thing a review can
// still act on.

import { readFileSync, readdirSync, existsSync, appendFileSync } from 'node:fs';
import { join, relative } from 'node:path';
import { execFileSync } from 'node:child_process';

const REPO = join(import.meta.dirname, '..');
const THRESHOLD = Number(process.env.COVERAGE_THRESHOLD ?? 80);
const BASE = process.env.COVERAGE_BASE ?? 'origin/main';
const REPORT_DIR = process.env.COVERAGE_DIR ?? join(REPO, 'artifacts/coverage');

// Scaffolded or design-time only. coverage.runsettings keeps these out of the report itself, so
// their lines are not coverable here and would be dropped silently; the list exists to name them
// in the output instead, because a reviewer has to know the diff contains code this number is
// saying nothing about.
const EXCLUDED = [/^Migrations\//, /^Data\/DesignTimeDbContextFactory\.cs$/];

const git = (...args) => execFileSync('git', args, { cwd: REPO, encoding: 'utf8' });

function findReports(dir) {
    if (!existsSync(dir)) return [];
    return readdirSync(dir, { recursive: true, withFileTypes: true })
        .filter(e => e.isFile() && e.name === 'coverage.cobertura.xml')
        .map(e => join(e.parentPath ?? e.path, e.name));
}

// Cobertura emits one <class> per type, so a file with three types appears three times and a
// partial class appears once per part. Merge on the file, taking the highest hit count seen for
// each line: two entries covering different halves of one file both count.
function parseCobertura(path) {
    const xml = readFileSync(path, 'utf8');
    const root = (xml.match(/<source>([^<]*)<\/source>/) ?? [, ''])[1];
    // Branches are read off the root element rather than summed: a <line> carries its taken/total
    // in a condition-coverage string, and the totals are already there to be read.
    const header = (xml.match(/<coverage[^>]*>/) ?? [''])[0];
    const headerNumber = name => Number((header.match(new RegExp(`${name}="([^"]*)"`)) ?? [, 0])[1]);
    const branches = { covered: headerNumber('branches-covered'), valid: headerNumber('branches-valid') };
    const files = new Map();
    let totals = { covered: 0, valid: 0 };

    for (const block of xml.split(/<class\s/).slice(1)) {
        const filename = (block.match(/filename="([^"]+)"/) ?? [])[1];
        if (!filename) continue;
        const lines = files.get(filename) ?? new Map();
        for (const m of block.matchAll(/<line number="(\d+)" hits="(\d+)"/g)) {
            const [no, hits] = [Number(m[1]), Number(m[2])];
            lines.set(no, Math.max(lines.get(no) ?? 0, hits));
        }
        files.set(filename, lines);
    }

    for (const lines of files.values())
        for (const hits of lines.values()) {
            totals.valid++;
            if (hits > 0) totals.covered++;
        }

    return { root, files, totals, branches };
}

// The lines this branch added or rewrote, from the unified diff's hunk headers. Diffed against the
// merge base rather than the tip of main, and with no `...`, so uncommitted work in the tree counts
// too — the agent reviews a working tree as often as a pushed branch.
function addedLines(base) {
    let mergeBase = base;
    try {
        mergeBase = git('merge-base', base, 'HEAD').trim();
    } catch {
        // No such ref (a shallow clone, a fork without the remote). Fall back to the tip.
    }
    const diff = git('diff', '--unified=0', '--diff-filter=d', mergeBase, '--', '*.cs');
    const byFile = new Map();
    let current = null;

    for (const line of diff.split('\n')) {
        const file = line.match(/^\+\+\+ b\/(.+)$/);
        if (file) {
            current = file[1];
            byFile.set(current, byFile.get(current) ?? new Set());
            continue;
        }
        const hunk = line.match(/^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))?/);
        if (hunk && current) {
            const start = Number(hunk[1]);
            const count = hunk[2] === undefined ? 1 : Number(hunk[2]);
            for (let i = 0; i < count; i++) byFile.get(current).add(start + i);
        }
    }
    return { byFile, mergeBase };
}

const reports = findReports(REPORT_DIR);
if (reports.length === 0) {
    console.error(`No coverage.cobertura.xml under ${relative(REPO, REPORT_DIR)}. Run scripts/coverage.sh.`);
    process.exit(2);
}

const { root, files, totals, branches } = parseCobertura(reports[0]);
const { byFile, mergeBase } = addedLines(BASE);
const sourceRoot = relative(REPO, root) + '/';   // e.g. src/FootballFormation.Core/

const measured = [];
const unmeasured = [];
const excluded = [];

for (const [path, added] of byFile) {
    if (path.startsWith('tests/')) continue;   // the tests are the instrument, not the subject
    if (!path.startsWith(sourceRoot)) {
        unmeasured.push(path);
        continue;
    }
    const key = path.slice(sourceRoot.length);
    if (EXCLUDED.some(rx => rx.test(key))) {
        excluded.push(path);
        continue;
    }
    // A line absent from the report is not coverable — a brace, a using, a field declaration, a
    // blank. Only lines the instrumenter counted are judged, so whitespace can't dilute the number.
    const lines = files.get(key) ?? new Map();
    const coverable = [...added].filter(n => lines.has(n));
    if (coverable.length === 0) continue;
    const hit = coverable.filter(n => lines.get(n) > 0);
    measured.push({
        path,
        total: coverable.length,
        hit: hit.length,
        missed: coverable.filter(n => lines.get(n) === 0).sort((a, b) => a - b),
    });
}

const pct = (hit, total) => (total === 0 ? 100 : (hit / total) * 100);
const changedHit = measured.reduce((n, f) => n + f.hit, 0);
const changedTotal = measured.reduce((n, f) => n + f.total, 0);

measured.sort((a, b) => pct(a.hit, a.total) - pct(b.hit, b.total));
const under = f => pct(f.hit, f.total) < THRESHOLD;

console.log(`Coverage of the change  (base ${mergeBase.slice(0, 12)}, threshold ${THRESHOLD}%)\n`);
console.log(`  Core overall: ${pct(totals.covered, totals.valid).toFixed(1)}%  (${totals.covered}/${totals.valid} lines)` +
    `, branches ${pct(branches.covered, branches.valid).toFixed(1)}%  (${branches.covered}/${branches.valid})`);

if (measured.length === 0) {
    console.log('  Changed lines: none measurable in Core.\n');
} else {
    console.log(`  Changed lines: ${pct(changedHit, changedTotal).toFixed(1)}%  (${changedHit}/${changedTotal})\n`);
    for (const f of measured) {
        const flag = under(f) ? 'FAIL' : '  ok';
        console.log(`  ${flag}  ${pct(f.hit, f.total).toFixed(1).padStart(5)}%  ${f.hit}/${f.total}  ${f.path}`);
        if (f.missed.length) console.log(`          uncovered lines: ${f.missed.join(', ')}`);
    }
}

// Named rather than silently dropped: a reviewer has to know which parts of the diff this number
// says nothing about. UI and Web have no unit tests by design — tests/ui and visual-check.sh are
// what cover them — so a change there is answered by those, not by this.
if (unmeasured.length) {
    console.log(`\n  Not measured here (no unit tests by design — see tests/ui and scripts/visual-check.sh):`);
    for (const p of unmeasured) console.log(`    ${p}`);
}
if (excluded.length) {
    console.log(`\n  Excluded (scaffolded or design-time):`);
    for (const p of excluded) console.log(`    ${p}`);
}

const failing = measured.filter(under);
const verdict =
    changedTotal > 0 && pct(changedHit, changedTotal) < THRESHOLD
        ? `FAIL: the change is ${pct(changedHit, changedTotal).toFixed(1)}% covered, under the ${THRESHOLD}% floor.`
        : failing.length
            ? `FAIL: ${failing.length} changed file(s) under the ${THRESHOLD}% floor.`
            : 'PASS';

console.log(`\n${verdict}`);

// The same answer on the run's own page, because that is where it will actually be read: the log
// is two clicks in and the Cobertura report is a download. Writes nothing outside Actions.
if (process.env.GITHUB_STEP_SUMMARY) {
    const shown = (hit, total) => `${pct(hit, total).toFixed(1)}% (${hit}/${total})`;
    const md = [
        '## Coverage',
        '',
        `Judged against \`${mergeBase.slice(0, 12)}\`, floor **${THRESHOLD}%** of the lines this change touched.`,
        '',
        '| Scope | Lines | Branches |',
        '| --- | ---: | ---: |',
        `| \`FootballFormation.Core\`, whole project | ${shown(totals.covered, totals.valid)} | ${shown(branches.covered, branches.valid)} |`,
        `| Lines this change touched | ${measured.length === 0 ? '—' : `**${shown(changedHit, changedTotal)}**`} | — |`,
        '',
    ];

    if (measured.length) {
        md.push('| | Changed file | Lines | Uncovered |', '| --- | --- | ---: | --- |');
        for (const f of measured) {
            md.push(`| ${under(f) ? '❌' : '✅'} | \`${f.path}\` | ${shown(f.hit, f.total)} | ` +
                `${f.missed.length ? f.missed.join(', ') : '—'} |`);
        }
        md.push('');
    } else {
        md.push('No measurable Core lines in this change.', '');
    }

    const details = (summary, paths) =>
        paths.length ? [`<details><summary>${summary} (${paths.length})</summary>`, '',
            ...paths.map(p => `- \`${p}\``), '', '</details>', ''] : [];
    md.push(
        ...details('Not measured here — no unit tests by design, see <code>tests/ui</code> and <code>scripts/visual-check.sh</code>', unmeasured),
        ...details('Excluded — scaffolded or design-time', excluded),
        `**${verdict}**`,
    );

    appendFileSync(process.env.GITHUB_STEP_SUMMARY, md.join('\n') + '\n');
}

process.exit(verdict === 'PASS' ? 0 : 1);
