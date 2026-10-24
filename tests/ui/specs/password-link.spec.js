// Password links end to end, copied and mailed; the rules that bound a link are PasswordLinkServiceTests'.
import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { test, expect } from '../fixtures.js';
import { BASE_URL, MAIL_DIR } from '../playwright.config.js';
import { goto, clickFor, openDialog, fillField, settle, userRow, addAdmin } from '../helpers.js';

const PASSWORD = 'linked-coach-1';

/** Every mail the app has written to the pickup directory for this address. */
const mailsTo = (address) => existsSync(MAIL_DIR)
  ? readdirSync(MAIL_DIR).map(f => JSON.parse(readFileSync(join(MAIL_DIR, f), 'utf8'))).filter(m => m.To === address)
  : [];

async function choosePassword(visitor, link) {
  await goto(visitor, link);
  await expect(visitor.getByRole('heading', { name: 'Choose a password' })).toBeVisible();
  await visitor.fill('input[name="password"]', PASSWORD);
  await visitor.fill('input[name="confirm"]', PASSWORD);
  await visitor.click('button[type="submit"]');

  await visitor.waitForURL(/\/login\?reset=true/);
  await expect(visitor.getByText('Your password is set', { exact: false })).toBeVisible();
  await settle(visitor);
}

async function askForReset(visitor, address) {
  await goto(visitor, '/forgot-password');
  await visitor.fill('input[name="email"]', address);
  await visitor.click('button[type="submit"]');
  await visitor.waitForURL(/\/forgot-password\?sent=true/);
  return visitor.locator('.inline-notice').innerText();
}

test('an account added without a password is let in by its copied link, once', async ({ page, context, visitor }) => {
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE_URL });
  const username = `linked-${Date.now()}`;

  await goto(page, '/users');
  await clickFor(
    page.getByRole('button', { name: 'Add User' }),
    () => expect(page.locator('.mud-dialog')).toBeVisible());
  const dialog = await openDialog(page);
  await fillField(dialog, 'Name', 'Linked coach');
  await fillField(dialog, 'Username', username);
  await dialog.getByRole('button', { name: 'Save' }).click();

  // No password was given, so the link that lets them in opens straight after.
  const link = page.locator('#sign-in-link');
  await expect(link).toBeVisible();
  await expect(userRow(page, username)).toBeAttached();

  await clickFor(
    page.getByRole('button', { name: 'Copy link' }),
    () => expect(page.getByText('Copied to clipboard', { exact: false })).toBeVisible());
  const copied = await page.evaluate(() => navigator.clipboard.readText());
  expect(copied).toBe((await link.textContent()).trim());

  await choosePassword(visitor, copied);

  // Spent: the same link now opens on the refusal, with no form to fill in.
  await goto(visitor, copied);
  await expect(visitor.getByText('This link has expired or has already been used')).toBeVisible();
  await expect(visitor.locator('input[name="password"]')).toHaveCount(0);
});

test('a forgotten password is answered the same for any address, and only an account\'s own gets a link', async ({ page, visitor }) => {
  const stamp = Date.now();
  const known = `mailed-${stamp}@example.com`;
  const unknown = `nobody-${stamp}@example.com`;
  await addAdmin(page, 'mailed', { email: known });

  await goto(visitor, '/login');
  await expect(visitor.getByRole('link', { name: 'Forgot your password?' })).toBeVisible();

  const forUnknown = await askForReset(visitor, unknown);
  const forKnown = await askForReset(visitor, known);
  expect(forKnown).toBe(forUnknown);

  await expect.poll(() => mailsTo(known).length, { message: 'the reset mail to reach the pickup directory' }).toBe(1);
  expect(mailsTo(unknown)).toEqual([]);

  const mailed = mailsTo(known)[0].Text.match(/https?:\/\/\S+/)[0];
  expect(mailed.startsWith(`${BASE_URL}/reset-password?token=`)).toBe(true);
  await choosePassword(visitor, mailed);
});

test('a link that was never issued offers no form', async ({ visitor }) => {
  await goto(visitor, '/reset-password?token=made-up');

  await expect(visitor.getByText('This link has expired or has already been used')).toBeVisible();
  await expect(visitor.locator('input[name="password"]')).toHaveCount(0);
  await expect(visitor.getByRole('link', { name: 'Back to sign in' })).toBeVisible();
});
