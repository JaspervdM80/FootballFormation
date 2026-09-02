// Measures every touch target the browser can reach on a phone, and fails the run when one is too
// small or sits behind dead space.
//
// Why this exists: docs/known_issues/touch-pwa.md is the longest section in docs/known_issues/, and
// every entry in it was reported from a touchline — twice, after the first fix was thought to be
// complete. All of those fixes are CSS, and until now nothing verified any of them. A MudBlazor
// upgrade or one more global `.mud-*` rule would undo any of them silently.
//
// Two measurements, because the reported bugs came in two shapes:
//
//   size       Every hit-testable element is at least 44x44 CSS px — Apple's minimum, and the floor
//              the app.css picker block is written to. This is what a 36px day cell, a 23px month
//              name, a 40px year button and a 36.5px "Annuleren" all failed.
//
//   clearance  The gap between a target and the nearest target beside or above it must be either
//              nothing (they meet, so there is no hole between them) or at least MIN_CLEAR. A gap
//              in between is a dead gutter: too narrow to see or aim around, wide enough to swallow
//              a tap. This is the measurement `document.elementFromPoint` cannot make — it reports
//              both neighbours as perfectly reachable while the gap between them belongs to
//              neither, and a mobile browser hands a tap that lands there to whichever neighbour
//              has the larger contact area. The 4px gutters between MudBlazor's 36px day cells were
//              exactly this, and the app.css fix was to drop the margins so the column pitch *is*
//              the target. The two checks together are what the pitch requirement amounts to: with
//              no dead gutter left, the distance between two column centres is the cell's own width.
//
// The size floor is also what guards the "Annuleren" bug in docs/known_issues/touch-pwa.md ("buttons need
// clear space above them"): the dead space above that button was what made a 36.5px target
// unhittable, but the number a harness can hold a line on is the 36.5px, not the 18px — a gap that
// size is unremarkable between two equally sized fields. So clearance catches the gutter, size
// catches the button, and the report below prints the measured gap above every target so the
// numbers those doc entries argue from stay in front of whoever reads the artifact next.
import { mkdirSync, writeFileSync } from 'node:fs';
import { clickFor, goto, gotoRendered, waitForStableBox, waitUntil } from './blazor.mjs';

export const MIN_TARGET = 44;

// Closer than this and two targets are one continuous surface — there is no hole to lose a tap in.
export const FLUSH = 1;

// A gap has to be at least this wide to read as a deliberate separation rather than a crack. Eight
// pixels is under a third of a fingertip, which is why anything smaller has to be zero instead.
// It is also MudBlazor's own spacing between two dialog buttons, so the tolerance below matters:
// that gap measures 7.9px on a subpixel layout and is not a defect.
export const MIN_CLEAR = 8;
export const SUBPIXEL = 0.5;

// A target may sit below MIN_TARGET only where the geometry provably cannot reach it. Each entry
// records what the CSS actually achieves today and why that is the ceiling, and the guard still
// fails if the element drops below the recorded number — an allowance is a floor, not an exemption.
export const RECORDED_FLOORS = [
  {
    viewport: '320x568',
    match: 'mud-day',
    floor: 41,
    why: 'Seven 44px columns need 308px and a 320px phone has 308px of usable width, so --dp-day '
      + 'settles at 41.7px. Every wider phone clears 44. See docs/known_issues/touch-pwa.md, "Centring the '
      + 'calendar was only half of it".',
  },
  {
    viewport: '844x390',
    match: 'mud-day',
    floor: 36,
    why: 'A landscape phone is short, not narrow: six 44px rows plus the picker chrome is 438px in '
      + 'a 390px-tall viewport, so --dp-day is sized by height and lands on MudBlazor\'s own 36px '
      + 'rather than making the month scroll. See docs/known_issues/touch-pwa.md, "The picker\'s flow is '
      + 'year -> month -> day".',
  },
  {
    viewport: '844x390',
    match: 'pitch-player',
    floor: 28,
    why: 'A landscape phone sizes the pitch by height: .pitch-constrained caps it at 65dvh, which '
      + 'on a 390px-tall viewport is a 190px-wide pitch, and five chips across a back line at 44px '
      + 'would need 220px. --chip-size lands on the 28px clamp minimum .pitch-compact declares.',
  },
  {
    viewport: '320x568',
    match: 'pitch-player',
    floor: 34,
    why: 'The chip is 13-15cqw of the pitch, and the live screen draws its pitch inside a card '
      + '(~227px on a 320px phone), where that percentage falls below the clamp minimum and the '
      + 'chip sits on it. Raising the minimum instead would overlap the wide positions, which are '
      + 'placed by percentage and already reach left: 8%.',
  },
  {
    viewport: '360x640',
    match: 'pitch-player',
    floor: 38,
    why: 'Same clamp on a wider card: 13cqw of a ~293px pitch. It scales with the phone, so every '
      + 'width above this one is closer to the floor rather than further from it.',
  },
];

// MudBlazor's real targets are rarely the semantic element: a day is a button, but a select's hit
// box is the .mud-input-control wrapper, and the bare <input> inside it is 32px of a 48px control.
const CANDIDATES = [
  'a[href]',
  'button',
  'input',
  'select',
  'textarea',
  '[role="button"]',
  '[role="menuitem"]',
  '[role="option"]',
  '[tabindex]:not([tabindex="-1"])',
  '.mud-input-control',
  '.mud-day',
  '.mud-picker-month',
  '.mud-picker-year',
  // Nothing semantic about either: a pitch chip is a div carrying @onclick, and a player in the
  // list beside it is a div you drag. Both are tapped, and neither would be measured otherwise.
  '.pitch-player',
  '.draggable-player',
].join(',');

/** Reads the geometry of every reachable target inside `rootSelector`. Runs in the browser. */
export async function measureTargets(page, rootSelector) {
  return page.evaluate(([root, candidates]) => {
    const host = document.querySelector(root);
    if (!host) return null;

    // Everything the element is actually drawn through: a scroll container that has moved past it,
    // a dialog with `overflow: hidden`, or the viewport edge. A target clipped to nothing is not on
    // screen at all.
    //
    // An ancestor's overflow only clips a descendant it is a containing block for, and getting that
    // wrong is not a detail here: MudBlazor locks the page while a dialog is open with
    // `overflow: hidden` on a <body> that is then shorter than the viewport. Take that at face value
    // and every fixed-position dialog and popover in the app reads as half scrolled out of view —
    // which is exactly the state this audit skips.
    const containsFor = (cs, positioning) => {
      const anchors = cs.transform !== 'none' || cs.filter !== 'none'
        || cs.willChange.includes('transform') || cs.contain.includes('paint');
      if (positioning === 'fixed') return anchors;
      if (positioning === 'absolute') return anchors || cs.position !== 'static';
      return true;
    };

    const clippers = [];
    const clipperId = (el) => {
      if (!clippers.includes(el)) clippers.push(el);
      return clippers.indexOf(el);
    };

    const visibleBox = (el) => {
      let box = { l: 0, t: 0, r: window.innerWidth, b: window.innerHeight };
      let positioning = getComputedStyle(el).position;
      let inside = -1;
      for (let p = el.parentElement; p; p = p.parentElement) {
        const cs = getComputedStyle(p);
        if (!containsFor(cs, positioning)) continue;
        if (cs.overflowX !== 'visible' || cs.overflowY !== 'visible') {
          const r = p.getBoundingClientRect();
          box = {
            l: Math.max(box.l, r.left), t: Math.max(box.t, r.top),
            r: Math.min(box.r, r.right), b: Math.min(box.b, r.bottom),
          };
          if (inside < 0) inside = clipperId(p);
        }
        // Past its containing block, the element is clipped the way that ancestor is.
        positioning = cs.position === 'fixed' || cs.position === 'absolute' ? cs.position : 'static';
      }
      return { ...box, inside };
    };

    const nodes = [...host.querySelectorAll(candidates)].filter((el) => {
      // A form control inside a MudBlazor input control is not the target — the wrapper is, and it
      // is in the list too. Keeping both would flag every 32px <input> in a 48px field.
      if (el.matches('input,select,textarea') && el.closest('.mud-input-control')) return false;
      if (getComputedStyle(el).pointerEvents === 'none') return false;
      if (el.checkVisibility && !el.checkVisibility({ opacityProperty: true, visibilityProperty: true })) return false;
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0;
    });

    const round = (n) => Math.round(n * 10) / 10;
    // Whichever of these an element carries says best what it is; classList order does not.
    const NAMES = ['mud-day', 'mud-picker-month', 'mud-picker-year', 'mud-icon-button', 'mud-button',
      'mud-input-control'];

    return nodes.map((el, i) => {
      const r = el.getBoundingClientRect();
      const clip = visibleBox(el);
      const v = {
        l: Math.max(r.left, clip.l), t: Math.max(r.top, clip.t),
        r: Math.min(r.right, clip.r), b: Math.min(r.bottom, clip.b),
      };
      const classes = [...el.classList];
      const name = NAMES.find(n => classes.includes(n)) ?? classes[0] ?? el.tagName.toLowerCase();
      // An icon-only control has no text of its own; say which field it belongs to instead, or the
      // report is a column of anonymous buttons.
      const field = el.closest('.mud-input-control')?.querySelector('label')?.textContent;
      const text = (el.textContent || el.getAttribute('aria-label') || el.title
        || (field ? `in ${field}` : '')).replace(/\s+/g, ' ').trim().slice(0, 24);
      // Nesting is normal in MudBlazor — the date field's calendar icon lives inside the input
      // control — and a target inside another target is not a gap between two targets.
      const nested = nodes.some((other, j) => j !== i && other.contains(el));
      return {
        name,
        label: text ? `${name} "${text}"` : `${name} <${el.tagName.toLowerCase()}>`,
        classes,
        pressable: el.matches('button,a[href],[role="button"]'),
        nested,
        // Two targets in different scroll containers are however far apart the scroll position
        // leaves them, which is not a fact about the layout.
        inside: clip.inside,
        x: round(r.left), y: round(r.top), w: round(r.width), h: round(r.height),
        vx: round(v.l), vy: round(v.t), vw: round(Math.max(0, v.r - v.l)), vh: round(Math.max(0, v.b - v.t)),
      };
    }).filter(t => t.vw > 0.5 && t.vh > 0.5
      // A focusable div inside a field is how MudBlazor renders a select's value; the target a
      // finger aims at is the control around it. Anything genuinely pressable inside a field — the
      // date field's calendar icon, a numeric field's steppers — stays.
      && (!t.nested || t.pressable));
  }, [rootSelector, CANDIDATES]);
}

const floorFor = (target, viewport, floors) => {
  const recorded = floors.find(f => f.viewport === viewport && target.classes.includes(f.match));
  return recorded ? { value: recorded.floor, recorded } : { value: MIN_TARGET, recorded: null };
};

const overlaps = (a1, a2, b1, b2) => Math.min(a2, b2) - Math.max(a1, b1) > 4;

/**
 * The nearest target above `t`, and the nearest to its left, measured on what is actually on screen.
 *
 * A pair is only comparable when neither of the two edges between them has been cut by a scroll
 * container: content sliding under a pinned footer leaves whatever distance the scroll position
 * happens to leave, and that is a boundary, not dead space. Measuring it would report a different
 * number on every run.
 */
const neighbours = (t, all) => {
  const before = (axis) => {
    const [tNear, tFull, tMinorNear, tMinorFar] = axis === 'y'
      ? [t.vy, t.y, t.vx, t.vx + t.vw] : [t.vx, t.x, t.vy, t.vy + t.vh];
    let best = null;
    for (const o of all) {
      if (o === t || o.nested || t.nested || o.inside !== t.inside) continue;
      const [oEnd, oFull, oMinorNear, oMinorFar] = axis === 'y'
        ? [o.vy + o.vh, o.y + o.h, o.vx, o.vx + o.vw] : [o.vx + o.vw, o.x + o.w, o.vy, o.vy + o.vh];
      if (!overlaps(tMinorNear, tMinorFar, oMinorNear, oMinorFar)) continue;
      if (oEnd > tNear + 0.5) continue;             // beside or after, not before
      if (!best || oEnd > best.end) best = { o, end: oEnd, cut: oEnd < oFull - 0.5 };
    }
    if (!best) return null;
    // The nearest neighbour is the one that matters, and if either edge between the two has been
    // cut by a scroll container there is no distance worth reporting: it is whatever the scroll
    // position left, and it moves on the next flick. Looking further up the page for an uncut pair
    // would measure past the thing actually next to it.
    if (best.cut || tNear > tFull + 0.5) return null;
    return { target: best.o, gap: Math.round((tNear - best.end) * 10) / 10 };
  };
  return { above: before('y'), left: before('x') };
};

/** Turns one scene's measurements into findings and a printable table. */
export function auditScene({ viewport, scene, targets, floors = RECORDED_FLOORS }) {
  const findings = [];
  const rows = [];

  for (const t of targets) {
    const { value: floor, recorded } = floorFor(t, viewport, floors);
    // A target scrolled half out of its container is not a small target; scroll it back and it is
    // the size it always was. Only measure what is whole.
    const whole = t.vw > t.w - 0.5 && t.vh > t.h - 0.5;
    if (whole && (t.w < floor || t.h < floor)) {
      findings.push({
        viewport, scene, check: 'size', label: t.label,
        key: `size|${t.name}|${t.w}x${t.h}`,
        detail: `${t.w}x${t.h} is under the ${floor}px floor`
          + (recorded ? ` recorded for it here (${recorded.why})` : ''),
      });
    }

    const near = neighbours(t, targets);
    for (const [side, n] of Object.entries(near)) {
      if (!n) continue;
      if (n.gap > FLUSH && n.gap < MIN_CLEAR - SUBPIXEL) {
        findings.push({
          viewport, scene, check: 'clearance', label: t.label,
          key: `clearance|${t.name}|${side}|${n.target.name}|${n.gap}`,
          detail: `${n.gap}px of dead space to the ${side} (${n.target.label}) — a gap has to be `
            + `nothing or at least ${MIN_CLEAR}px, never a crack between the two`,
        });
      }
    }

    rows.push({
      target: t.label,
      size: `${t.w}x${t.h}`,
      floor: recorded ? `${floor} (recorded)` : `${floor}`,
      above: near.above ? `${near.above.gap}px` : '—',
      left: near.left ? `${near.left.gap}px` : '—',
      clipped: whole ? '' : 'partly scrolled out',
    });
  }

  // One broken rule shows up on every cell of a calendar. Report the shape once, with the count —
  // 42 copies of the same sentence buries the other five findings under it.
  const collapsed = [];
  for (const f of findings) {
    const seen = collapsed.find(c => c.key === f.key);
    if (seen) seen.also = (seen.also ?? 0) + 1;
    else collapsed.push(f);
  }
  for (const f of collapsed) {
    if (f.also) f.detail += ` — and ${f.also} more like it on this screen`;
  }

  return { findings: collapsed, rows };
}

const table = (rows) => {
  const cols = ['target', 'size', 'floor', 'above', 'left', 'clipped'];
  const head = { target: 'Target', size: 'Size', floor: 'Floor', above: 'Gap above', left: 'Gap left', clipped: 'Note' };
  const line = r => `| ${cols.map(c => String(r[c] ?? '')).join(' | ')} |`;
  return [line(head), `|${cols.map(() => '---').join('|')}|`, ...rows.map(line)].join('\n');
};

// The three widths docs/known_issues/touch-pwa.md argues from. 320 is the narrowest phone the picker has to
// fit, 360 is the common one, and landscape is short rather than narrow — a different failure, and
// the reason app.css hides the picker's date line to buy the year button its 44px.
const VIEWPORTS = [
  { name: '320x568', width: 320, height: 568 },
  { name: '360x640', width: 360, height: 640 },
  // Wide enough to lay the sections out on the bar itself, and the one viewport where the pickers
  // and sign out are on it — so the app-bar scene measures something there that it cannot elsewhere.
  { name: '844x390', width: 844, height: 390 },
];

const rx = (nl, en) => new RegExp(`${nl}|${en}`, 'i');

/**
 * Walks the screens a thumb reaches on a phone-sized touch context and audits each one. The dialog
 * and the picker came first because every entry in the Touch / PWA section was found in one of them
 * — the dialog is the app's longest form and the only one filled in at a touchline, and the picker
 * is inside it. The games list came next because it is the page a phone opens most, and the row of
 * icon buttons on every card is the densest cluster of targets in the app. The trainings pair
 * followed: a row of a different shape, and the app's only switch. The last four are the chrome
 * every navigation goes through, the squad's own row geometry, and the two screens where the target
 * is a pitch chip sized by a clamp() rather than a button sized by a token.
 */
export async function auditTouchTargets({ browser, base, out, liveGame, onError = () => {} }) {
  // Named rather than defaulted: a missing path would quietly drop the two scenes that need it, and
  // an audit that silently measures less is the failure this whole harness exists to prevent.
  if (!liveGame) throw new Error('auditTouchTargets needs liveGame — see visual-check.mjs');

  const dir = `${out}/touch`;
  mkdirSync(dir, { recursive: true });

  const findings = [];
  const report = [`# Touch targets`, '', `Floor ${MIN_TARGET}px; a gap must be <= ${FLUSH}px or >= ${MIN_CLEAR}px.`, ''];

  for (const viewport of VIEWPORTS) {
    const context = await browser.newContext({
      viewport: { width: viewport.width, height: viewport.height },
      // A real phone, so MudBlazor takes the touch paths and the CSS sees the viewport it would.
      hasTouch: true,
      isMobile: true,
      deviceScaleFactor: 2,
    });
    const page = await context.newPage();
    page.on('console', m => { if (m.type() === 'error') onError(`[console ${viewport.name}] ${m.text()}`); });
    page.on('pageerror', e => onError(`[pageerror ${viewport.name}] ${e.message}`));

    await gotoRendered(page, `${base}/dev/login`);
    await goto(page, `${base}/games`);

    let scenes = 0;

    /**
     * `required` names classes the scene has to have actually measured. A target scrolled out of the
     * viewport is skipped rather than reported, so without this a scene passes while measuring
     * nothing it was added for — which is how the pitch scenes below shipped measuring no chip.
     */
    const audit = async (scene, root, required = []) => {
      scenes++;
      const targets = await measureTargets(page, root);
      if (!targets) throw new Error(`${viewport.name}: nothing matched ${root} for "${scene}"`);
      for (const name of required) {
        if (!targets.some(t => t.classes.includes(name)))
          throw new Error(`${viewport.name}: "${scene}" measured no .${name}`);
      }
      const audited = auditScene({ viewport: viewport.name, scene, targets });
      findings.push(...audited.findings);
      report.push(`## ${viewport.name} — ${scene}`, '', table(audited.rows), '');
      await page.screenshot({ path: `${dir}/${viewport.name}-${scene.replace(/[ ,]+/g, '-')}.png` });
    };

    /** Brings an element to the middle of the viewport, and waits for the scroll to stop. */
    const scrollTo = async (selector) => {
      const target = page.locator(selector).first();
      await target.evaluate(el => el.scrollIntoView({ block: 'center' }));
      await waitForStableBox(target);
    };

    // The page itself, before anything is opened on top of it: the header's Add button and the
    // action row on every game card. Scoped to the main content rather than the document, so the
    // app bar and the drawer stay a scene of their own rather than arriving inside this one.
    //
    // Waited for rather than assumed, and counted rather than merely found. The header renders
    // before the games do, so measuring on arrival would sometimes measure a list that is still a
    // spinner — and a list with no card in it measures the Add button, finds nothing wrong, and
    // passes. Six is the widest the row ever gets, and it only gets there on the day of the match.
    await waitUntil(page, async () =>
      await page.locator('.game-cards .game-actions').first().locator('.action-btn').count() >= 6, {
      what: "the seeded game's six match-day action buttons — visual-check.mjs seeds a game dated "
        + 'today, and without that date the Live button never appears and the widest action row in '
        + 'the app goes unmeasured',
    });
    await audit('games list', '.app-main');

    // Every wait below is on the thing itself rather than on a clock. MudBlazor scales a dialog and
    // a popover in, and measuring geometry mid-animation is how a full-width sheet reads 86% of its
    // width — so each one is measured only once its box has stopped moving.
    const dialog = page.locator('.mud-dialog');
    const popover = page.locator('.mud-picker-popover.mud-popover-open');

    await clickFor(page.getByRole('button', { name: rx('toevoegen', 'add') }).first(),
      () => dialog.isVisible());
    await waitForStableBox(dialog);

    // The form is taller than any phone, and a target below the fold is clipped out of the
    // measurement — so measure it from both ends. The half nobody sees first is the half with the
    // action row in it.
    await audit('new match dialog', '.mud-dialog');

    const content = page.locator('.mud-dialog-content').first();
    await content.evaluate(el => { el.scrollTop = el.scrollHeight; });
    await waitUntil(page, async () => {
      const at = await content.evaluate(el => el.scrollTop + el.clientHeight >= el.scrollHeight - 1);
      return at;
    }, { what: 'the dialog to reach the bottom of its scroll' });
    await audit('new match dialog, scrolled down', '.mud-dialog');

    // The calendar icon at the end of the date field — the only way into the picker on a phone.
    await clickFor(page.locator('.mud-dialog .mud-input-adornment button').first(),
      () => popover.isVisible());
    await waitForStableBox(popover);
    await audit('date picker, days', '.mud-picker-popover.mud-popover-open');

    // The two views behind the same popover, each reached by its own button: the month grid off the
    // month name, and the year list off the toolbar's year. Each swaps the popover's contents, so
    // the wait is for the new view's own elements rather than for the popover, which never left.
    await clickFor(page.locator('.mud-picker-calendar-header-transition').first(),
      async () => await popover.locator('.mud-picker-month').count() > 0);
    await waitForStableBox(popover);
    await audit('date picker, months', '.mud-picker-popover.mud-popover-open');

    await clickFor(page.locator('.mud-picker-datepicker-toolbar .mud-button-root').first(),
      async () => await popover.locator('.mud-picker-year').count() > 0);
    await waitForStableBox(popover);
    await audit('date picker, years', '.mud-picker-popover.mud-popover-open');

    // The trainings section, reached by navigating rather than by closing what is open: the picker
    // and the dialog both go with the page, and the next audit wants a clean one anyway.
    //
    // Two screens rather than one. The list is a different shape from the games list — two buttons
    // on a row instead of up to six, right-aligned on their own line rather than filling it — and
    // the dialog is the only form in the app with a switch on it, whose thumb is a target no other
    // scene here measures.
    await goto(page, `${base}/trainings`);
    await waitUntil(page, async () =>
      await page.locator('.training-row .action-btn').count() >= 2, {
      what: 'the seeded training rows — visual-check.mjs seeds two, one of them cancelled, and a '
        + 'list with no row in it measures the Add button, finds nothing wrong, and passes',
    });
    await audit('trainings list', '.app-main');

    await clickFor(page.getByRole('button', { name: rx('toevoegen', 'add') }).first(),
      () => dialog.isVisible());
    await waitForStableBox(dialog);
    await audit('new training dialog', '.mud-dialog');

    // The chrome, on its own rather than inside a page's scene: it is tapped on every navigation,
    // and on a phone the hamburger is the only way to any section at all. The drawer is a checkbox
    // with no circuit behind it, so the label is what a thumb hits.
    //
    // Below 700px this measures the hamburger and the title link and nothing else: the bar hides the
    // season picker there and its own overflow clips whatever is left past the right-hand edge. The
    // drawer scene below carries the pickers; the sign-out button is app-bar only and is measured at
    // the landscape width, where the bar keeps every item it started with (issue #137).
    await goto(page, `${base}/players`);
    await audit('app bar', '.mud-appbar', ['app-title-link']);

    // The squad. Same --action-btn-size as the game cards, in a different row geometry — which is
    // what makes the clearance rule the part worth measuring here rather than the size.
    await waitUntil(page, async () =>
      await page.locator('.players-table .row-actions .mud-icon-button').count() >= 2, {
      what: 'the seeded squad rows — visual-check.mjs seeds four players, and a table with no row '
        + 'in it measures the Add button, finds nothing wrong, and passes',
    });
    await audit('squad', '.app-main', ['player-name-cell']);

    // The drawer last on this page, and closed by navigating away rather than by the hamburger: once
    // it is open it covers the label that opened it, and the checkbox holding it open is page state
    // a navigation discards anyway.
    //
    // A closed drawer is parked off the left of the screen and hidden with visibility, so it has no
    // box to read until it opens — which makes the box itself the signal that it has.
    const drawerLink = page.locator('.app-drawer a').first();
    await clickFor(page.locator('label.nav-hamburger'),
      async () => ((await drawerLink.boundingBox())?.x ?? -1) >= 0);
    await waitForStableBox(page.locator('.app-drawer'));
    await audit('drawer', '.app-drawer', ['mud-nav-link']);

    // The formation builder and the live screen, the two places a chip is the target. Both are
    // sized by a clamp() on container width, so the narrowest phone is where they are smallest —
    // and the live screen is the one tapped one-handed, under time pressure, sometimes in the rain.
    //
    // Two passes each, for the same reason the match dialog gets two: the pitch is below the fold on
    // every one of these viewports, and a target clipped out of view is skipped. Measured from the
    // top alone, both scenes passed while measuring no chip at all.
    await goto(page, `${base}${liveGame.replace('/live', '/formation')}`);
    await waitUntil(page, async () => await page.locator('.pitch .pitch-player').count() > 0, {
      what: "the seeded line-up's chips on the pitch",
    });
    await audit('formation builder', '.app-main', ['draggable-player']);
    await scrollTo('.pitch');
    await audit('formation builder, pitch', '.app-main', ['pitch-player']);

    await goto(page, `${base}${liveGame}`);
    await waitUntil(page, async () => await page.locator('.live-actions').count() > 0, {
      what: 'the live screen of a match under way — visual-check.mjs kicks one off, and before '
        + 'kick-off none of the controls this scene exists to measure are on the page',
    });
    // Scrolled to for the same reason the line-up is: a 390px-tall landscape viewport is filled by
    // the scoreboard alone, so the two buttons a goal is logged with start below the fold there.
    await scrollTo('.live-actions');
    await audit('live match, controls', '.app-main', ['live-action-btn']);
    await scrollTo('.live-lineup .pitch');
    await audit('live match, line-up', '.app-main', ['pitch-player']);

    // Asserted, not logged: a scene that stopped running would otherwise say so only in a number
    // nobody reads. The drawer is on every viewport now, so every viewport audits the same fifteen.
    if (scenes !== 15)
      throw new Error(`${viewport.name}: audited ${scenes} screens, expected 15`);
    console.log(`${viewport.name.padEnd(8)} audited ${scenes} screens`);
    await context.close();
  }

  writeFileSync(`${dir}/report.md`, report.join('\n'));
  return findings;
}
