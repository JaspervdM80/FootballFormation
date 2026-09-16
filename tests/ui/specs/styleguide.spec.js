// /styleguide is the design system, and this is what stops it drifting from the app.
//
// The gallery is a hand-written list (DesignTokens.Groups) of a set the stylesheets own, so the two
// can fall out of step silently — which is exactly how docs/theming.md came to document a token that
// no longer exists and to miss five that do. Rather than compare one list against another, this
// reads the custom properties the browser actually has in effect and asserts each one is on the
// page: it covers ClubTheme's head block and theme.css in a single pass, with no list of its own to
// maintain.
import { test, expect } from '../fixtures.js';
import { gotoRendered } from '../helpers.js';

/**
 * Every `--custom-property` declared by a `:root` rule in any same-origin stylesheet, minus
 * MudBlazor's own `--mud-*` namespace: MudThemeProvider injects around 150 of those, and they are
 * generated from ClubTheme's palette rather than authored here.
 */
async function declaredTokens(page) {
  return page.evaluate(() => {
    const names = new Set();
    for (const sheet of document.styleSheets) {
      let rules;
      // A cross-origin sheet throws on .cssRules; the app serves its own, so this is belt and braces.
      try {
        rules = sheet.cssRules;
      } catch {
        continue;
      }
      for (const rule of rules) {
        if (!rule.selectorText?.split(',').some(s => s.trim() === ':root')) continue;
        for (const property of rule.style) {
          if (property.startsWith('--') && !property.startsWith('--mud-')) names.add(property);
        }
      }
    }
    return [...names].sort();
  });
}

test('every token the stylesheets declare has a swatch on the style guide', async ({ page }) => {
  await gotoRendered(page, '/styleguide');
  await expect(page.getByRole('heading', { name: 'Style Guide', exact: false }).first()).toBeVisible();

  const declared = await declaredTokens(page);
  // Guards the probe itself: a selector change that stopped matching :root would otherwise leave an
  // empty set passing every assertion below.
  expect(declared.length, 'no custom properties found — is the probe still matching :root?').toBeGreaterThan(30);

  const shown = await page.locator('.sg-token').allInnerTexts();
  const missing = declared.filter(token => !shown.includes(token));

  expect(missing, `tokens with no swatch — add them to DesignTokens.Groups: ${missing.join(', ')}`).toEqual([]);
});

test('the style guide draws no token the stylesheets have dropped', async ({ page }) => {
  await gotoRendered(page, '/styleguide');

  const declared = await declaredTokens(page);
  const shown = await page.locator('.sg-token').allInnerTexts();
  const orphaned = shown.filter(token => !declared.includes(token));

  expect(orphaned, `swatches for tokens nothing declares: ${orphaned.join(', ')}`).toEqual([]);
});

test('the shared component classes still paint', async ({ page }) => {
  await gotoRendered(page, '/styleguide');

  // A scoped-CSS mistake renders these as unstyled markup rather than failing, so assert a property
  // each class is the only source of — see .claude/skills/styling-and-css.
  const badge = page.locator('.badge-venue-home').first();
  await expect(badge).toBeVisible();
  await expect(badge).toHaveCSS('border-top-width', '1px');

  const action = page.locator('.action-btn.action-live').first();
  await expect(action).toBeVisible();
  await expect(action).not.toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});
