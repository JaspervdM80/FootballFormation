// What the app bar does when it runs out of room, at the widths where it does.
//
// This is a regression spec rather than a feature one. #137 was seven nav links laid out against a
// breakpoint derived for five: the season picker, the language picker and sign out went off the
// right-hand edge, with no scroll and — above 700px — no drawer to reach them from either. Nothing
// failed. The touch audit skips a target clipped out of the viewport by design, and the specs that
// click `.topbar-nav a` only ever name links near the left of the bar.
//
// So this measures the two things that were quietly untrue: the bar does not overflow, and whatever
// it drops is in the drawer. Neither is visible from a unit test, and an eighth section would
// otherwise break both again with every check green.
import { test, expect } from '../fixtures.js';
import { clickFor, gotoRendered } from '../helpers.js';

// Signed in as an admin, which is the case that overflowed: seven sections rather than three, plus
// the account name and the sign-out button. The two shapes the issue named, and one with room.
const SHAPES = [
  ['a landscape phone', 844, 390],
  ['a 1280px laptop', 1280, 800],
  ['a full-width desktop', 1600, 900],
];

for (const [shape, width, height] of SHAPES) {
  test(`the season picker, the language picker and sign out stay on the bar on ${shape}`, async ({ page }) => {
    await page.setViewportSize({ width, height });
    await gotoRendered(page, '/players');

    // In the viewport, not merely in the DOM — being in the DOM is exactly what the clipped ones were.
    await expect(page.locator('.mud-appbar .season-picker')).toBeInViewport();
    await expect(page.locator('.mud-appbar .language-picker')).toBeInViewport();
    await expect(page.locator('.logout-btn')).toBeInViewport();

    // And the bar has nothing hidden past its own right edge, which is the measurement the touch
    // harness cannot make: it stops at the viewport, and the overflow starts inside the toolbar.
    await expect
      .poll(async () => page.locator('.mud-appbar .mud-toolbar')
        .evaluate(el => el.scrollWidth - el.clientWidth))
      .toBeLessThanOrEqual(0);
  });
}

test('a section the bar has no room for is in the drawer rather than nowhere', async ({ page }) => {
  // 844x390 leaves room for one nav link, so the other six are the drop this is about.
  await page.setViewportSize({ width: 844, height: 390 });
  await gotoRendered(page, '/players');

  const { shown, all } = await page.evaluate(() => {
    const nav = document.querySelector('.topbar-nav');
    const floor = nav.getBoundingClientRect().bottom + 0.5;
    const links = [...nav.querySelectorAll('a')];
    return {
      all: links.map(a => a.textContent.trim()),
      // A dropped link has a box like any other — it is on the wrapped row, directly under the one
      // the clip keeps, which is why this reads the bottom edge rather than the right one.
      shown: links.filter(a => a.getBoundingClientRect().bottom <= floor).map(a => a.textContent.trim()),
    };
  });

  const dropped = all.filter(name => !shown.includes(name));
  expect(dropped.length).toBeGreaterThan(0);

  const drawerLinks = page.locator('.app-drawer .mud-nav-link');
  expect(await drawerLinks.evaluateAll(els => els.map(el => el.textContent.trim())))
    .toEqual(expect.arrayContaining(dropped));

  // The hamburger is on every width now, which is what makes dropping a link safe rather than a
  // section nobody can reach.
  const first = drawerLinks.filter({ hasText: dropped[0] }).first();
  await expect(first).not.toBeInViewport();
  await clickFor(page.locator('label.nav-hamburger'), () => expect(first).toBeInViewport());
});

// #141, the other layout sized for the content it had: below 599.98px the users table stacks into
// per-row cards, and an `auto` role column took whatever ApplicationAdmin asked for — the "You"
// badge then painted over the role badge, and the name ran out of its own cell. 375px is where it
// showed, which is why this sets a width: the mobile project's Pixel 7 is 412 and never reproduced it.
test('a user card keeps the name, the "You" badge and the role in their own columns', async ({ page }) => {
  await page.setViewportSize({ width: 375, height: 812 });
  await gotoRendered(page, '/users');

  const row = page.locator('.users-table .mud-table-body .mud-table-row').first();
  await expect(row).toBeVisible();

  // The seeded account is the only ApplicationAdmin and it is the signed-in one, so its row is the
  // one carrying both badges — an ordinary admin's row shows neither collision.
  const box = await row.evaluate(el => {
    const rect = sel => el.querySelector(sel)?.getBoundingClientRect() ?? null;
    return {
      badgeRight: rect('.cell-name .badge-teal')?.right ?? null,
      nameCellRight: rect('.cell-name')?.right ?? null,
      roleLeft: rect('.cell-role')?.left ?? null,
    };
  });

  expect(box.badgeRight).not.toBeNull();
  expect(box.badgeRight).toBeLessThanOrEqual(box.nameCellRight + 0.5);
  expect(box.nameCellRight).toBeLessThanOrEqual(box.roleLeft + 0.5);
});
