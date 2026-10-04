// Waiting for a Blazor Server page, without guessing how long it takes.
//
// A Blazor Server page renders twice: a static prerender, then again once the SignalR circuit
// connects. The prerender is a complete, correct-looking page whose buttons are visible, enabled and
// wired to nothing. Screenshot it and you capture a half-built page; click it and the click is
// swallowed with no error anywhere.
//
// tests/ui/helpers.js carries the same rule for the Playwright suite. The two are deliberately not
// shared: they are separate npm packages with different dependencies, and a dozen lines duplicated
// beats a cross-package import. Change one, look at the other.

// InteractiveShell's marker reads "pending" in the prerender and "live" once the circuit's first render, handlers and all, has landed.
// An interactive page without the shell has no marker, so its inert prerender would pass as ready.
const READY = () => !document.querySelector('[data-circuit="pending"]');

/** Navigates and waits for the page to be interactive rather than merely painted. A page with no circuit is ready once loaded. */
export async function goto(page, url) {
  await page.goto(url, { waitUntil: 'load' });
  await page.waitForFunction(READY, null, { timeout: 30_000 });
  // A layout measured straight after the load would otherwise be measured in the fallback font.
  await page.evaluate(() => document.fonts.ready.then(() => true));
}

/**
 * Waits until an element has stopped moving and resizing.
 *
 * MudBlazor scales a dialog and a popover in, so anything measured the instant it becomes visible is
 * measured mid-animation — a phone-width sheet reads about 86% of its final width. Two identical
 * readings a frame apart is the cheap, exact answer, and it costs whatever the animation actually
 * takes instead of whatever a sleep guessed.
 */
export async function waitForStableBox(locator, { timeout = 10_000 } = {}) {
  const deadline = Date.now() + timeout;
  let previous = null;
  while (Date.now() < deadline) {
    const box = await locator.boundingBox().catch(() => null);
    const key = box && `${Math.round(box.x)},${Math.round(box.y)},${Math.round(box.width)},${Math.round(box.height)}`;
    if (key && key === previous) return;
    previous = key;
    await locator.page().waitForTimeout(50);
  }
  throw new Error(`${locator} never stopped moving`);
}

/**
 * Clicks until it takes, then waits for the result to settle.
 *
 * `ready` is an async predicate describing what the click should have caused. A click that lands in
 * the prerender window does nothing, so it is repeated rather than assumed — which also means this
 * must never be used for an action that is not safe to repeat.
 */
export async function clickFor(locator, ready, { tries = 3, settle = 5_000 } = {}) {
  for (let attempt = 0; attempt < tries; attempt++) {
    await locator.click({ timeout: 15_000 });
    const deadline = Date.now() + settle;
    while (Date.now() < deadline) {
      if (await ready().catch(() => false)) return;
      await locator.page().waitForTimeout(50);
    }
  }
  throw new Error(`clicked ${locator} ${tries} times and nothing happened`);
}

/** Polls a predicate until it holds. For waiting on something nothing had to be clicked for. */
export async function waitUntil(page, predicate, { timeout = 15_000, what = 'condition' } = {}) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    if (await predicate().catch(() => false)) return;
    await page.waitForTimeout(50);
  }
  throw new Error(`timed out waiting for ${what}`);
}

/** Both languages, because the UI is Dutch by default and English is a resource-key fallback. */
export const rx = (nl, en) => new RegExp(`${nl}|${en}`, 'i');

// The seeded admin's own password. Only ever changed on a throwaway database.
const SEED_PASSWORD = 'admin';
const NEW_PASSWORD = 'visualcheck123';

/**
 * Signs in as admin through /dev/login — Development-only, loopback-only, and minting the same principal /auth/login does.
 *
 * A freshly seeded admin still holds the password it was created with, which locks every other route to /settings until it
 * changes. `changeSeededPassword` gets past that, and is only for a throwaway database: against anybody's real one it throws
 * instead of changing their password.
 */
export async function signInAsAdmin(page, base, { changeSeededPassword = false } = {}) {
  await goto(page, `${base}/dev/login`);
  await goto(page, `${base}/settings`);

  const notice = page.getByText(rx('wachtwoord waarmee het is aangemaakt', 'still uses the password'));
  if (!(await notice.count())) return;
  if (!changeSeededPassword) {
    throw new Error('The admin is still on the seeded password, which pins every page to /settings. Change it in the app first.');
  }

  const passwordFields = page.locator('input[type="password"]');
  await passwordFields.nth(0).fill(SEED_PASSWORD);
  await passwordFields.nth(1).fill(NEW_PASSWORD);
  await passwordFields.nth(2).fill(NEW_PASSWORD);
  // Clicked once, deliberately: changing a password is not idempotent, so a retry would be made
  // with a password that is no longer the current one.
  await page.getByRole('button', { name: rx('wachtwoord wijzigen', 'change password') }).click();
  // Waited for the landing, not for the notice to go: the notice clears on re-render, before the
  // circuit drops onto /login, and signing in on that signal has Playwright abandon our navigation
  // for the circuit's ("Navigation to /dev/login is interrupted by another navigation to /login").
  await page.waitForURL(/\/login(\?|$)/, { timeout: 20_000 });

  await goto(page, `${base}/dev/login`);
  console.log('changed the seeded admin password and signed back in');
}
