// Drives the running app in a real browser and writes a screenshot per page.
//
// This is the "look at it" half of testing. The unit tests cover the domain rules; nothing else in
// the repo checks that a page actually renders, so a layout that collapses or a component that
// throws on first render passes CI and is only caught by eye. Each run also collects browser
// console errors, which is where a Blazor render failure surfaces.
//
// It then measures rather than looks: touch-targets.mjs re-walks the new-match dialog and its date
// picker on three phone-sized viewports and fails the run on a target under 44px or a dead gap
// between two of them. That half is not about a page rendering at all — it is the only thing
// holding the Touch / PWA fixes in docs/known_issues.md in place.
//
// Started by scripts/visual-check.sh, which boots the app first. Run that, not this.
import { chromium } from 'playwright';
import { mkdirSync } from 'node:fs';
import { auditTouchTargets } from './touch-targets.mjs';

const BASE = process.env.VISUAL_BASE_URL ?? 'http://127.0.0.1:5228';
const OUT = process.env.VISUAL_OUT_DIR ?? 'artifacts/visual';
const CHROME = process.env.VISUAL_CHROMIUM ?? '/opt/pw-browsers/chromium';

// The seeded admin's own password. Only ever used against a throwaway database.
const SEED_PASSWORD = 'admin';
const NEW_PASSWORD = 'visualcheck123';

const SEED_PLAYERS = [
  ['Anouk', 'de Vries', 7],
  ['Fenna', 'Bakker', 10],
  ['Sanne', 'Jansen', 4],
  ['Lotte', 'Visser', 1],
];

const PAGES = [
  ['home', '/'],
  ['players', '/players'],
  ['games', '/games'],
  ['stats', '/stats'],
  ['users', '/users'],
  ['settings', '/settings'],
];

// Both languages, because the UI is Dutch by default and English is a resource-key fallback.
const rx = (nl, en) => new RegExp(`${nl}|${en}`, 'i');

mkdirSync(OUT, { recursive: true });

const browser = await chromium.launch({ executablePath: CHROME });
const context = await browser.newContext({
  viewport: { width: 1440, height: 900 },
  deviceScaleFactor: 2,
});
const page = await context.newPage();

const errors = [];
page.on('console', m => { if (m.type() === 'error') errors.push(`[console] ${m.text()}`); });
page.on('pageerror', e => errors.push(`[pageerror] ${e.message}`));

// Development-only, loopback-only route that mints the same principal /auth/login does.
const signIn = async () => {
  await page.goto(`${BASE}/dev/login`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(800);
};

await signIn();

// A freshly seeded admin still holds the password it was created with, and that locks every other
// route to /settings until it changes. Get past it, or every screenshot is the same page.
await page.goto(`${BASE}/settings`, { waitUntil: 'networkidle' });
await page.waitForTimeout(1500);
const passwordFields = page.locator('input[type="password"]');
if (await page.getByText(rx('wachtwoord waarmee het is aangemaakt', 'still uses the password')).count()) {
  await passwordFields.nth(0).fill(SEED_PASSWORD);
  await passwordFields.nth(1).fill(NEW_PASSWORD);
  await passwordFields.nth(2).fill(NEW_PASSWORD);
  await page.getByRole('button', { name: rx('wachtwoord wijzigen', 'change password') }).click();
  await page.waitForTimeout(2000);
  // Changing a password rotates the account's security stamp, and OnValidatePrincipal rejects the
  // cookie that was issued before it — so sign in again rather than browsing as an anonymous visitor.
  await signIn();
  console.log('changed the seeded admin password and signed back in');
}

// Seed through the UI rather than the database, so the captures show real rendered rows and the
// seeding itself exercises the dialogs.
await page.goto(`${BASE}/players`, { waitUntil: 'networkidle' });
await page.waitForTimeout(1500);
if (!(await page.getByText(SEED_PLAYERS[0][0]).count())) {
  for (const [first, last, shirt] of SEED_PLAYERS) {
    await page.getByRole('button', { name: rx('speler toevoegen', 'add player') }).click();
    // MudMenu items are not menuitem-role elements — they render as .mud-menu-item-text.
    await page.locator('.mud-popover-open').getByText(rx('nieuwe speler', 'new player')).click();
    await page.waitForTimeout(800);
    // Field order in PlayerDialog: first name, surname, shirt number, then the position selects.
    const inputs = page.locator('.mud-dialog input');
    await inputs.nth(0).fill(first);
    await inputs.nth(1).fill(last);
    await inputs.nth(2).fill(String(shirt));
    await page.locator('.mud-dialog').getByRole('button', { name: rx('opslaan', 'save') }).click();
    await page.waitForTimeout(1200);
    console.log(`seeded ${first} ${last} (#${shirt})`);
  }
}

for (const [name, path] of PAGES) {
  await page.goto(BASE + path, { waitUntil: 'networkidle' });
  // A Blazor Server page renders twice: static prerender, then again once the circuit connects.
  // Screenshotting between the two catches a half-built page.
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${OUT}/${name}.png`, fullPage: true });
  const heading = await page.locator('h1, h4, .mud-typography-h4').first().textContent().catch(() => '');
  console.log(`${name.padEnd(9)} ${path.padEnd(10)} ${(heading ?? '').trim().slice(0, 40)}`);
}

console.log('\nMeasuring touch targets...');
const tooSmall = await auditTouchTargets({ browser, base: BASE, out: OUT, onError: e => errors.push(e) });

await browser.close();

if (tooSmall.length) {
  console.error(`\n${tooSmall.length} touch target problem(s):`);
  for (const f of tooSmall) {
    console.error(`  ${f.viewport}  ${f.scene}  ${f.label}\n    ${f.check}: ${f.detail}`);
  }
}
if (errors.length) {
  console.error(`\n${errors.length} browser error(s):\n${errors.join('\n')}`);
}
if (tooSmall.length || errors.length) {
  process.exit(1);
}
console.log(`\nNo browser errors, every touch target clears its floor.`);
console.log(`Screenshots in ${OUT}/, touch measurements in ${OUT}/touch/report.md`);
