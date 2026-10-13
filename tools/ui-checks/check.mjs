import { chromium } from 'playwright';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

const baseUrl = process.env.GOLD_PREVIEW_URL ?? 'http://127.0.0.1:5186';
const screenshotDirectory = path.resolve('artifacts/ui-checks');
await fs.mkdir(screenshotDirectory, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1360, height: 920 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));

async function capture(name) {
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.screenshot({ path: path.join(screenshotDirectory, `${name}.png`), fullPage: true });
}

try {
  await page.goto(baseUrl);
  await page.getByRole('heading', { name: 'Operations', exact: true }).waitFor();
  assert.match(await page.locator('footer').innerText(), /DAMiner v\d+\.\d+\.\d+/,
    'The version expression must render a numeric version instead of literal Razor text');
  // Settings is longer than one screen: its rail must reach the page bottom,
  // while navigation remains available after scrolling to the last section.
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByRole('heading', { name: 'Application updates', exact: true }).waitFor();
  const rail = await page.evaluate(() => ({
    bottom: document.querySelector('.sidebar').getBoundingClientRect().bottom,
    pageBottom: document.querySelector('.app-shell').getBoundingClientRect().bottom,
    viewportHeight: window.innerHeight,
  }));
  assert.ok(rail.pageBottom > rail.viewportHeight, 'Use a page that needs scrolling');
  assert.ok(Math.abs(rail.bottom - rail.pageBottom) <= 1,
    'The sidebar background must extend to the bottom of a long page');
  await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight));
  await page.waitForFunction(() => window.scrollY > 0);
  const navigation = await page.getByRole('navigation').boundingBox();
  assert.ok(navigation.y >= 0 && navigation.y + navigation.height <= rail.viewportHeight,
    'Navigation must stay visible when the page is scrolled to the bottom');
  await page.getByRole('button', { name: 'Operations', exact: true }).click();
  await page.getByRole('heading', { name: 'Operations', exact: true }).waitFor();
  await page.evaluate(() => window.scrollTo(0, 0));
  assert.equal(await page.getByRole('navigation').getByRole('button', { name: 'Record setup', exact: true }).count(), 0,
    'A saved recipe must keep recording out of daily navigation');
  assert.equal(await page.getByRole('row').count(), 5);
  const homeEnable = page.getByRole('checkbox', { name: 'Enable Rowan', exact: true });
  assert.equal(await homeEnable.count(), 1, 'Operations must expose character enable controls');
  await homeEnable.uncheck();
  await page.getByRole('row').filter({ has: page.getByText('Rowan', { exact: true }) }).getByText('Disabled', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Characters', exact: true }).click();
  await page.getByRole('heading', { name: 'Your saved roster', exact: true }).waitFor();
  assert.equal(await page.getByRole('checkbox', { name: 'Enable Rowan', exact: true }).isChecked(), false);
  await page.getByRole('checkbox', { name: 'Enable Rowan', exact: true }).check();
  await page.getByRole('row').filter({ has: page.getByText('Rowan', { exact: true }) }).getByText('Connected', { exact: true }).waitFor();
  await page.getByRole('button', { name: 'Operations', exact: true }).click();
  await page.getByRole('heading', { name: 'Operations', exact: true }).waitFor();
  assert.equal(await homeEnable.isChecked(), true);
  await capture('operations');

  await page.getByRole('button', { name: 'Characters', exact: true }).click();
  for (const name of ['Rowan', 'Celandine', 'Muir']) {
    await page.getByRole('checkbox', { name: `Can craft bars: ${name}`, exact: true }).uncheck();
    await page.getByRole('row').filter({ has: page.getByText(name, { exact: true }) })
      .getByText('Gold donor', { exact: true }).waitFor();
  }
  assert.equal(await page.getByRole('cell', { name: 'Gold donor', exact: true }).count(), 3);
  await capture('donor-roster');
  await page.getByRole('button', { name: 'Operations', exact: true }).click();

  await page.getByRole('button', { name: 'Go', exact: false }).click();
  await page.locator('input[aria-label="Enable Rowan"]:disabled').waitFor();
  await page.getByRole('heading', { name: 'Completed', exact: true }).waitFor({ timeout: 30000 });
  await page.locator('.run-strip').getByText('Delivered 7 bars; 46,499,996 gold remains', { exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Go', exact: false }).isEnabled(), true);
  await capture('completed');

  await page.getByRole('button', { name: 'History', exact: true }).click();
  await page.getByRole('heading', { name: 'Verified operations', exact: true }).waitFor();
  assert.ok(await page.getByRole('cell', { name: 'Verified', exact: true }).count() > 0);
  const craftRows = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'craft', exact: true }) });
  assert.ok(await craftRows.count() > 0);
  for (const row of await craftRows.all()) {
    assert.equal(await row.getByRole('cell', { name: 'Aisling', exact: true }).count(), 1);
  }

  await capture('history');
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByRole('heading', { name: 'Saved bank setup', exact: true }).waitFor();
  await page.getByRole('heading', { name: 'Application updates', exact: true }).waitFor();
  assert.equal(await page.getByLabel('GitHub release repository', { exact: true }).isDisabled(), true);
  assert.equal(await page.getByRole('button', { name: 'Check for updates', exact: true }).isDisabled(), true);
  await page.getByText('Updates disabled in simulated preview.', { exact: true }).waitFor();
  await page.getByText('Advanced setup', { exact: true }).click();
  await page.getByRole('button', { name: 'Review recording setup', exact: true }).click();
  await page.getByRole('heading', { name: 'Show it once. Run it next time.', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: /Launch and record|Resume recording/ }).isEnabled(), false);
  assert.match(await page.getByRole('status').innerText(), /Recording off/);
  await capture('recording-status');

  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByLabel('Deposit wait (seconds)', { exact: true }).fill('120');
  await page.getByLabel('Banker character', { exact: true }).fill('Collector');
  await page.getByLabel('Banker map ID', { exact: true }).fill('901');
  await page.getByLabel('Banker X', { exact: true }).fill('19');
  await page.getByLabel('Banker Y', { exact: true }).fill('12');
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  assert.equal(await page.getByLabel('Deposit wait (seconds)', { exact: true }).inputValue(), '120');
  await capture('settings');
  await page.setViewportSize({ width: 760, height: 880 });
  await capture('settings-compact');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);
  await page.setViewportSize({ width: 1360, height: 920 });

  await page.getByRole('button', { name: 'Operations', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Enable Muir', exact: true }).uncheck();
  await page.getByRole('button', { name: 'Go', exact: false }).click();
  await page.locator('.run-strip').getByText('Delivered 5 bars; 47,499,997 gold remains', { exact: true }).waitFor({ timeout: 30000 });
  const disabledRow = page.getByRole('row').filter({ has: page.getByText('Muir', { exact: true }) });
  await disabledRow.getByText('Disabled', { exact: true }).waitFor();
  assert.equal(await disabledRow.getByRole('cell', { name: '99,999,999', exact: true }).count(), 1);

  await page.getByRole('row').filter({ has: page.getByText('Aisling', { exact: true }) })
    .getByRole('cell', { name: '901 · 18,12', exact: true }).waitFor();

  // Use an enabled saved character as a local banker, keeping the stale external
  // destination to prove local delivery uses Abel bank instead.
  await page.getByRole('checkbox', { name: 'Enable Muir', exact: true }).check();
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByLabel('Banker mode', { exact: true }).selectOption('Local');
  await page.getByLabel('Banker character', { exact: true }).fill('Muir');
  await page.getByLabel('Deposit location', { exact: true }).waitFor();
  assert.equal(await page.getByLabel('Deposit location', { exact: true }).inputValue(), 'Abel bank · Lamont');
  assert.equal(await page.getByLabel('Deposit NPC name', { exact: true }).count(), 0);
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  await page.locator('label').filter({ hasText: 'Deposit wait (seconds)' }).locator('input:disabled').waitFor();
  assert.equal(await page.getByLabel('Deposit wait (seconds)', { exact: true }).isDisabled(), true);
  await capture('local-banker-settings');
  await page.getByRole('button', { name: 'Operations', exact: true }).click();
  await page.getByRole('cell', { name: 'Banker', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Go', exact: false }).click();
  await page.locator('.run-strip').getByText('Delivered 5 bars; 47,499,997 gold remains', { exact: true }).waitFor({ timeout: 30000 });
  const bankerRow = page.getByRole('row').filter({ has: page.getByText('Muir', { exact: true }) });
  await bankerRow.getByRole('cell', { name: '0', exact: true }).waitFor();
  await page.getByRole('row').filter({ has: page.getByText('Aisling', { exact: true }) })
    .getByRole('cell', { name: '167 · 5,8', exact: true }).waitFor();
  await bankerRow.getByRole('cell', { name: '167 · 6,8', exact: true }).waitFor();
  await page.getByRole('button', { name: 'History', exact: true }).click();
  const deposits = page.getByRole('row').filter({ has: page.getByRole('cell', { name: 'bar deposit', exact: true }) })
    .filter({ has: page.getByRole('cell', { name: 'Verified', exact: true }) });
  await deposits.nth(4).waitFor();
  assert.equal(await deposits.count(), 5);
  await capture('local-banker-history');

  await page.setViewportSize({ width: 430, height: 900 });
  await page.getByRole('button', { name: 'Operations', exact: true }).click();
  await capture('compact');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true);

  assert.deepEqual(errors, []);
  console.log('UI checks passed: full-height sidebar and sticky navigation, roster controls, donor runs, disabled character skipped, custom external banker destination, local banker at Abel bank and five verified deposits, recorder isolation, settings, compact layout.');
} catch (error) {
  // Print before browser cleanup so a slow Chrome shutdown cannot hide the failure.
  console.error(error);
  await capture('failure');
  throw error;
} finally {
  await browser.close();
}
