// A test for the tests.
//
// The failure mode that matters in a UI suite is not a test that breaks — it is a test that stops
// testing. Almost every assertion here that proves an *absence* is written as a count of zero, and
// a count of zero is exactly what a selector returns when the class it names no longer exists. So a
// rename in the app turns "an anonymous visitor is offered no Delete button" into a sentence that is
// true because nothing is called that any more, and the suite stays green while the check is gone.
//
// This is the guard: every app class a selector in this directory names has to still exist in the
// app's own source. The names are read out of the tests rather than kept in a list, which drifts.
//
// It reads the source rather than the browser on purpose. Checking in a browser would mean putting
// the app into the exact state each class appears in, which is most of the rest of this directory;
// reading the source catches the rename, which is the thing that actually happens, and costs
// milliseconds. What it deliberately does not prove is that the class still renders where the test
// looks for it — that is what the specs themselves are for.
import { readdirSync, readFileSync } from 'node:fs';
import { basename, extname, join } from 'node:path';
import { test, expect } from '../fixtures.js';

const UI_TESTS = join(import.meta.dirname, '..');
const SOURCE = join(import.meta.dirname, '../../../src');

/** The string literals in a script, past its comments and regular expressions — either can hold a stray quote. */
function stringsIn(code) {
  const strings = [];
  let previous = '';
  for (let i = 0; i < code.length; i++) {
    const c = code[i];
    if (c === '/' && code[i + 1] === '/') {
      i = code.indexOf('\n', i);
      if (i < 0) break;
    } else if (c === '/' && code[i + 1] === '*') {
      i = code.indexOf('*/', i + 2) + 1;
    } else if (c === '/' && (previous === '' || '(,=:[!&|?{};'.includes(previous))) {
      for (let inClass = false, done = false; !done && ++i < code.length;) {
        if (code[i] === '\\') i++;
        else if (code[i] === '[') inClass = true;
        else if (code[i] === ']') inClass = false;
        else if (code[i] === '/' && !inClass) done = true;
      }
      previous = '/';
    } else if (c === '\'' || c === '"' || c === '`') {
      let text = '';
      for (i++; i < code.length && code[i] !== c; i++) {
        if (code[i] === '\\') {
          text += code[++i];
        } else if (c === '`' && code[i] === '$' && code[i + 1] === '{') {
          for (let depth = 0; i < code.length; i++) {
            if (code[i] === '{') depth++;
            else if (code[i] === '}' && --depth === 0) break;
          }
        } else {
          text += code[i];
        }
      }
      strings.push(text);
      previous = c;
    } else if (!/\s/.test(c)) {
      previous = c;
    }
  }
  return strings;
}

// A class in a selector follows the start, a combinator, a bracket, or a tag name: `ff.auth` is a cookie, `label.mud-switch` a selector.
const CLASS_CHAIN = /(?:^|[\s>+~,(]|\b(?:a|button|input|label|details|summary|select|option|div|span|li|ul|table|tr|td|th|form|textarea|img|svg|nav|main|header|section|p|pre|h[1-6]))((?:\.[a-z][\w-]*)+)/g;

/** Every app-owned class the tests name. MudBlazor's are not ours to rename, and an upgrade that drops one fails a spec for real. */
function classesTheTestsUse() {
  const files = [...readdirSync(join(UI_TESTS, 'specs')).map(name => join('specs', name)), 'helpers.js', 'fixtures.js',
    'global-setup.js'].filter(file => extname(file) === '.js' && basename(file) !== 'selectors.spec.js');

  const names = new Set();
  for (const file of files) {
    const code = readFileSync(join(UI_TESTS, file), 'utf8');
    for (const text of stringsIn(code)) {
      for (const [, list] of text.matchAll(/class="([^"]+)"/g)) list.split(/\s+/).forEach(name => names.add(name));
      // Attribute values and paths go first: a slash outside a bracket is a URL or a file, not a selector.
      const selector = text.replace(/\[[^\]]*\]/g, '[]');
      if (selector.includes('/')) continue;
      for (const [, chain] of selector.matchAll(CLASS_CHAIN)) chain.split('.').filter(Boolean).forEach(name => names.add(name));
    }
    for (const [, name] of code.matchAll(/toHaveClass\(\/([a-z][\w-]*)\//g)) names.add(name);
  }
  return [...names].filter(name => !name.startsWith('mud-')).sort();
}

/** Every markup, code, stylesheet and script file in the app, read once. */
function appSource() {
  const wanted = new Set(['.razor', '.cs', '.css', '.js']);
  return readdirSync(SOURCE, { recursive: true, withFileTypes: true })
    .filter(entry => entry.isFile() && wanted.has(extname(entry.name)))
    .map(entry => join(entry.parentPath ?? entry.path, entry.name))
    .filter(path => !/[\\/](bin|obj|node_modules)[\\/]/.test(path))
    .map(path => readFileSync(path, 'utf8'))
    .join('\n');
}

test('every class name these tests rely on still exists in the app', () => {
  const source = appSource();
  expect(source.length, 'the app source should have been found and read').toBeGreaterThan(1000);

  const used = classesTheTestsUse();
  // Guards the reader itself: a regression in it would otherwise leave nothing to check, and pass.
  expect(used.length, 'no class names were read out of the tests').toBeGreaterThan(100);

  // Bounded on both sides by anything that cannot continue a class name, so `pitch` does not answer
  // for `pitch-empty`, nor `score-value` for `live-score-value`.
  const missing = used.filter(name => !new RegExp(`(?<![\\w-])${name}(?![\\w-])`).test(source));

  expect(missing, 'renamed or removed in the app — the tests naming them now assert nothing').toEqual([]);
});
