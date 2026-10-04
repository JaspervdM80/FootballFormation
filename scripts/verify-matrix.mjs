// The verify-ui matrix as numbers: every route at desktop and phone width, as a visitor and as an admin. Started by
// scripts/verify-matrix.sh, which boots the app first unless VERIFY_BASE_URL names one already running. Run that, not this.
import { chromium } from 'playwright';
import { existsSync, mkdirSync, readFileSync, readdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { goto, signInAsAdmin } from './blazor.mjs';

const BASE = process.env.VERIFY_BASE_URL ?? 'http://127.0.0.1:5228';
const OUT = process.env.VERIFY_OUT_DIR ?? 'artifacts/verify';
const FRESH = process.env.VERIFY_FRESH_DATABASE === '1';
const REPO = join(dirname(fileURLToPath(import.meta.url)), '..');

const PREINSTALLED = '/opt/pw-browsers/chromium';
const CHROME = process.env.VISUAL_CHROMIUM ?? (existsSync(PREINSTALLED) ? PREINSTALLED : undefined);

const WIDTHS = {
  desktop: { width: 1280, height: 800 },
  mobile: { width: 375, height: 812 },
};

// The same short list tests/ui/fixtures.js ignores: the PWA plumbing, never a render failure.
const IGNORED = [/manifest\.webmanifest/i, /Failed to load resource.*favicon/i, /service ?worker/i];

// Read off the @page directives, so a new page is in the matrix the day it exists. Routes with an id need data, so they are named.
function pageRoutes(folder = join(REPO, 'src')) {
  return readdirSync(folder, { withFileTypes: true }).flatMap(entry => {
    const path = join(folder, entry.name);
    if (entry.isDirectory()) return ['bin', 'obj'].includes(entry.name) ? [] : pageRoutes(path);
    if (!entry.name.endsWith('.razor')) return [];
    return [...readFileSync(path, 'utf8').matchAll(/@page\s+"([^"]+)"/g)].map(m => m[1]).filter(route => !route.includes('{'));
  });
}

const routes = process.argv.length > 2 ? process.argv.slice(2) : pageRoutes().sort();
if (routes.length === 0) {
  console.error('No routes to visit — has the @page pattern stopped matching?');
  process.exit(1);
}
mkdirSync(OUT, { recursive: true });

const browser = await chromium.launch(CHROME ? { executablePath: CHROME } : { channel: 'chromium' });

async function visit(width, asAdmin) {
  const context = await browser.newContext({ viewport: WIDTHS[width] });
  const page = await context.newPage();
  if (asAdmin) await signInAsAdmin(page, BASE, { changeSeededPassword: FRESH });

  const cells = [];
  for (const route of routes) {
    const errors = [];
    const onConsole = m => { if (m.type() === 'error' && !IGNORED.some(p => p.test(m.text()))) errors.push(m.text()); };
    const onPageError = e => errors.push(e.message);
    page.on('console', onConsole);
    page.on('pageerror', onPageError);

    let failed = null;
    try {
      await goto(page, `${BASE}${route}`);
    } catch (e) {
      failed = e.message.split('\n')[0];
    }

    const landed = new URL(page.url()).pathname;
    const measured = failed ? { overflow: false, controls: [] } : await page.evaluate(() => ({
      overflow: document.documentElement.scrollWidth > window.innerWidth,
      controls: [...document.querySelectorAll('button, a[href], [role="button"], input, select, textarea')]
        .filter(el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden')
        .map(el => (el.getAttribute('aria-label') || el.getAttribute('title') || el.textContent || el.getAttribute('name') || el.tagName)
          .replace(/\s+/g, ' ').trim().slice(0, 40))
        .filter(Boolean),
    }));

    const name = `${route === '/' ? 'home' : route.slice(1).replaceAll('/', '-')}-${width}-${asAdmin ? 'admin' : 'visitor'}`;
    if (!failed) await page.screenshot({ path: join(OUT, `${name}.png`), fullPage: true });

    page.off('console', onConsole);
    page.off('pageerror', onPageError);
    cells.push({ route, width, who: asAdmin ? 'admin' : 'visitor', landed, failed, errors, ...measured });
  }

  await context.close();
  return cells;
}

const cells = [];
for (const width of Object.keys(WIDTHS)) {
  cells.push(...await visit(width, false), ...await visit(width, true));
}
await browser.close();

const problems = [];
for (const cell of cells) {
  const redirected = cell.landed !== cell.route ? `→ ${cell.landed}` : '';
  const notes = [
    cell.failed && `FAILED: ${cell.failed}`,
    cell.errors.length && `${cell.errors.length} console error(s): ${cell.errors.join(' | ')}`,
    cell.overflow && 'horizontal overflow',
  ].filter(Boolean);
  console.log(`${cell.route.padEnd(22)} ${cell.width.padEnd(8)} ${cell.who.padEnd(8)} ${redirected.padEnd(12)} ${notes.join('; ') || 'ok'}`);

  const turnedAway = cell.who === 'admin' && cell.landed.startsWith('/login') && !cell.route.startsWith('/login');
  if (notes.length || turnedAway) problems.push(cell);
}

// What only an admin is shown — the list to read for anything that should not be there, or should be. The chrome's share is
// printed once, and a route the visitor was turned away from has no visitor page to compare.
const adminOnly = (route, width) => {
  const find = who => cells.find(c => c.route === route && c.width === width && c.who === who);
  if (find('visitor').landed !== route) return null;
  const visitor = new Set(find('visitor').controls);
  return [...new Set(find('admin').controls)].filter(control => !visitor.has(control));
};

console.log('\nControls an admin sees that a visitor does not:');
for (const width of Object.keys(WIDTHS)) {
  const perRoute = routes.map(route => [route, adminOnly(route, width)]).filter(([, controls]) => controls);
  const chrome = perRoute.length ? perRoute.map(([, controls]) => controls).reduce((a, b) => a.filter(c => b.includes(c))) : [];
  console.log(`  every page (${width}): ${chrome.join(', ') || 'nothing'}`);
  for (const [route, controls] of perRoute) {
    const own = controls.filter(control => !chrome.includes(control));
    if (own.length) console.log(`  ${route} (${width}): ${own.join(', ')}`);
  }
}

console.log(`\n${cells.length} cells, ${problems.length} with a problem. Screenshots in ${OUT}.`);
process.exit(problems.length ? 1 : 0);
