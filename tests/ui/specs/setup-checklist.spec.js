// The "Get your team ready" card on / is for an admin of a team that has not been set up yet. Each
// test makes its own team, named per attempt: dismissing is stored on the team for good, so a CI
// retry against the same database would otherwise start from a card already gone.
import { test, expect } from '../fixtures.js';
import { BASE_URL } from '../playwright.config.js';
import { addTeam, clickFor, goto, showTeam } from '../helpers.js';

const checklist = page => page.locator('.setup-checklist');

// The circuit's first render is drawn before Home has loaded the setup, so an absence read off the live
// page passes whatever the server holds. The prerender runs the load to completion, with the same cookies.
const prerenderedHome = async page => (await page.request.get('/')).text();

async function freshTeam(page, name) {
  const team = `${name}-${Date.now() % 100_000}`;
  await addTeam(page, team);
  await showTeam(page, team);
  await goto(page, '/');
  return team;
}

test('a new team shows its admin what to set up, and the link to send the parents', async ({ page, context, visitor }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE_URL });
  const team = await freshTeam(page, 'Setup');

  await expect(checklist(page)).toContainText('0 of 4 done');
  await expect(checklist(page).locator('a.setup-step[href="/preferences"]')).toBeVisible();
  await expect(checklist(page).locator('a.setup-step[href="/players"]')).toBeVisible();
  await expect(checklist(page).locator('a.setup-step[href="/settings"]')).toBeVisible();
  await expect(checklist(page).locator('a.setup-step[href="/games"]')).toBeVisible();

  const link = (await page.locator('#setup-team-link').textContent()).trim();
  expect(new URL(link).pathname).toBe(`/team/gjs/${team.toLowerCase()}`);
  await clickFor(
    checklist(page).getByRole('button', { name: 'Copy team link' }),
    () => expect(page.locator('#setup-link-copied')).toBeVisible());
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(link);

  // The same team's home page, seen by someone who is not its admin.
  expect(await prerenderedHome(page)).toContain('setup-checklist');
  await goto(visitor, new URL(link).pathname);
  expect(await prerenderedHome(visitor)).toContain('home-header');
  expect(await prerenderedHome(visitor)).not.toContain('setup-checklist');
});

for (const [how, control] of [
  ['the close button', page => checklist(page).getByRole('button', { name: 'Dismiss' })],
  ['"Hide this list"', page => checklist(page).getByRole('button', { name: 'Hide this list' })],
]) {
  test(`dismissing with ${how} takes the card away for good`, async ({ page }) => {
    await freshTeam(page, 'Dismiss');
    await expect(checklist(page)).toBeVisible();
    expect(await prerenderedHome(page)).toContain('setup-checklist');

    await clickFor(control(page), () => expect(checklist(page)).toHaveCount(0));

    expect(await prerenderedHome(page)).not.toContain('setup-checklist');
  });
}
