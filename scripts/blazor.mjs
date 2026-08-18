// Waiting for a Blazor Server page, without guessing how long it takes.
//
// A Blazor Server page renders twice: a static prerender, then again once the SignalR circuit
// connects. The prerender is a complete, correct-looking page whose buttons are visible, enabled and
// wired to nothing. Screenshot it and you capture a half-built page; click it and the click is
// swallowed with no error anywhere.
//
// The two obvious signals for "it is ready now" are both wrong. Measured on /settings in this app:
//
//   domcontentloaded, window.Blazor already true   0 of 12 buttons have handlers
//   the circuit's first WebSocket frame            still 0 — that frame is the handshake
//   a _bl_ attribute is present                    15 handlers bound, ~230ms in
//
// Blazor's client renderer writes `_bl_<guid>` onto every element it attaches an event to, so that
// attribute is the signal, and it is the one these helpers wait for.
//
// tests/ui/helpers.js carries the same rule for the Playwright suite. The two are deliberately not
// shared: they are separate npm packages with different dependencies, and a dozen lines duplicated
// beats a cross-package import. Change one, look at the other.

/** True once Blazor has bound handlers on this page — see the note above. It only ever sees
    MudBlazor's own controls: Blazor writes the attribute for handlers it registers on the element,
    and a plain `<button @onclick>` of ours never gets one. */
const HANDLERS_BOUND = () => [...document.querySelectorAll('button,a,input')]
  .some(el => el.getAttributeNames().some(name => name.startsWith('_bl_')));

/** Navigates and waits for the page to be interactive rather than merely painted. */
export async function goto(page, url) {
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(HANDLERS_BOUND, null, { timeout: 30_000 });
}

/**
 * Navigates and waits for the markup only, for a page with no handler to wait for: one rendered
 * without a circuit, or one that renders no MudBlazor control for this visitor. Blazor stamps
 * `_bl_` on MudBlazor's own controls and not on a plain `<button @onclick>` of ours, so `goto`
 * would wait thirty seconds on those pages for a signal that never arrives.
 */
export async function gotoRendered(page, url) {
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  // Until the page stops fetching, not merely until it paints. A page that does open a circuit
  // starts negotiating during this window, and navigating away mid-handshake aborts it — which
  // surfaces as a "Failed to complete negotiation" console error and fails the run. WebSockets do
  // not count towards networkidle, so an established circuit does not hold this open.
  await page.waitForLoadState('networkidle');
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
