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
import { existsSync, mkdirSync } from 'node:fs';
import { auditTouchTargets } from './touch-targets.mjs';
import { clickFor, goto } from './blazor.mjs';

const BASE = process.env.VISUAL_BASE_URL ?? 'http://127.0.0.1:5228';
const OUT = process.env.VISUAL_OUT_DIR ?? 'artifacts/visual';

// A Claude Code web container ships a Chromium at this path, at a revision that will not match
// whatever `playwright` resolves to — so use the one that is there, and fall back to Playwright's
// own everywhere else. `undefined` is the fallback on purpose: it means "resolve it yourself",
// which is what a CI runner needs after `npx playwright install chromium`.
const PREINSTALLED = '/opt/pw-browsers/chromium';
const CHROME = process.env.VISUAL_CHROMIUM ?? (existsSync(PREINSTALLED) ? PREINSTALLED : undefined);

// The seeded admin's own password. Only ever used against a throwaway database.
const SEED_PASSWORD = 'admin';
const NEW_PASSWORD = 'visualcheck123';

const SEED_PLAYERS = [
  ['Anouk', 'de Vries', 7],
  ['Fenna', 'Bakker', 10],
  ['Sanne', 'Jansen', 4],
  ['Lotte', 'Visser', 1],
];

const SEED_OPPONENT = 'SV Zwaluwen';

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
const signIn = () => goto(page, `${BASE}/dev/login`);

await signIn();

// A freshly seeded admin still holds the password it was created with, and that locks every other
// route to /settings until it changes. Get past it, or every screenshot is the same page.
await goto(page, `${BASE}/settings`);
const notice = page.getByText(rx('wachtwoord waarmee het is aangemaakt', 'still uses the password'));
if (await notice.count()) {
  const passwordFields = page.locator('input[type="password"]');
  await passwordFields.nth(0).fill(SEED_PASSWORD);
  await passwordFields.nth(1).fill(NEW_PASSWORD);
  await passwordFields.nth(2).fill(NEW_PASSWORD);
  // Clicked once, deliberately: changing a password is not idempotent, so a retry would be made
  // with a password that is no longer the current one. Waited on rather than slept through — the
  // change rotates the security stamp, OnValidatePrincipal rejects the cookie issued before it, and
  // the circuit is dropped onto the login page, which is the observable outcome.
  await page.getByRole('button', { name: rx('wachtwoord wijzigen', 'change password') }).click();
  // Waited for the landing, not for the notice to go. Both follow the same change, but the notice
  // clears the moment the component re-renders and the drop onto /login happens after that — so
  // signing in on the notice signal starts a navigation to /dev/login while the circuit's own
  // navigation is still in flight, and Playwright abandons ours: "Navigation to /dev/login is
  // interrupted by another navigation to /login". That kills the run before its first screenshot.
  // The URL is the only signal that says the drop has finished rather than that it is coming.
  await page.waitForURL(/\/login(\?|$)/, { timeout: 20_000 });

  await signIn();
  console.log('changed the seeded admin password and signed back in');
}

// Seed through the UI rather than the database, so the captures show real rendered rows and the
// seeding itself exercises the dialogs.
await goto(page, `${BASE}/players`);
if (!(await page.getByText(SEED_PLAYERS[0][0]).count())) {
  const dialog = page.locator('.mud-dialog');
  for (const [first, last, shirt] of SEED_PLAYERS) {
    // MudMenu items are not menuitem-role elements — they render as .mud-menu-item-text.
    const newPlayer = page.locator('.mud-popover-open').getByText(rx('nieuwe speler', 'new player'));
    await clickFor(page.getByRole('button', { name: rx('speler toevoegen', 'add player') }),
      () => newPlayer.isVisible());
    await clickFor(newPlayer, () => dialog.isVisible());

    // Field order in PlayerDialog: first name, surname, shirt number, then the position selects.
    const inputs = dialog.locator('input');
    await inputs.nth(0).fill(first);
    await inputs.nth(1).fill(last);
    await inputs.nth(2).fill(String(shirt));
    await clickFor(dialog.getByRole('button', { name: rx('opslaan', 'save') }),
      async () => await page.locator('.mud-dialog').count() === 0, { settle: 10_000 });
    console.log(`seeded ${first} ${last} (#${shirt})`);
  }
}

// One game, dated today. An empty /games is a paragraph of text — no card to screenshot and no
// action row to measure — and the date is the whole point of which card it is: the Live button
// appears only on the day of the match, so match day is the day the action row carries six
// buttons instead of five. That is the row a coach uses, and the one worth holding a floor under.
await goto(page, `${BASE}/games`);
if (!(await page.getByText(SEED_OPPONENT).count())) {
  const dialog = page.locator('.mud-dialog');
  const popover = page.locator('.mud-picker-popover.mud-popover-open');

  await clickFor(page.getByRole('button', { name: rx('toevoegen', 'add') }).first(),
    () => dialog.isVisible());
  await dialog.locator('input').first().fill(SEED_OPPONENT);

  // The dialog proposes the season's next match day, which is up to a week out. Walk the picker
  // back to today instead: its cell is the one MudBlazor marks .mud-current, and it is either in
  // the month the picker opened on or the one before it — never further, so one step back at most.
  await clickFor(dialog.locator('.mud-input-adornment button').first(), () => popover.isVisible());
  const todayCell = popover.locator('.mud-day.mud-current');
  if (!(await todayCell.count())) {
    await clickFor(popover.locator('.mud-picker-calendar-header-switch .mud-icon-button').first(),
      async () => await todayCell.count() > 0);
  }
  await clickFor(todayCell, async () => await popover.count() === 0);

  await clickFor(dialog.getByRole('button', { name: rx('opslaan', 'save') }),
    async () => await page.locator('.mud-dialog').count() === 0, { settle: 10_000 });
  console.log(`seeded a game vs ${SEED_OPPONENT}, today`);
}

for (const [name, path] of PAGES) {
  // A Blazor Server page renders twice: static prerender, then again once the circuit connects.
  // Screenshotting between the two catches a half-built page, so goto waits for the second — see
  // blazor.mjs. A page that is still loading its data says so with a spinner, which is a finding
  // rather than something to sleep through.
  await goto(page, BASE + path);
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
