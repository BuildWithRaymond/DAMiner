import { chromium } from 'playwright';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

// Capture only the simulation host. No live profiles or game clients are used.
const directory = path.resolve('artifacts/banker-comparison');
await fs.mkdir(directory, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1360, height: 920 } });
const errors = [];
page.on('pageerror', error => errors.push(error.message));

async function captureBanker(name) {
  const panel = page.locator('section.panel').filter({
    has: page.getByRole('heading', { name: 'Banker', exact: true })
  });
  await panel.screenshot({ path: path.join(directory, `${name}.png`) });
}

try {
  await page.goto('http://127.0.0.1:5186');
  await page.getByRole('heading', { name: 'Operations', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByRole('heading', { name: 'Saved bank setup', exact: true }).waitFor();

  await page.getByLabel('Banker mode', { exact: true }).selectOption('External');
  await page.getByLabel('Banker character', { exact: true }).fill('Vaultkeeper');
  await page.getByLabel('Banker map ID', { exact: true }).fill('901');
  await page.getByLabel('Banker X', { exact: true }).fill('19');
  await page.getByLabel('Banker Y', { exact: true }).fill('12');
  await page.getByLabel('Bars per trade', { exact: true }).fill('3');
  await page.getByLabel('Deposit wait (seconds)', { exact: true }).fill('120');
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  assert.equal(await page.getByLabel('Banker character', { exact: true }).inputValue(), 'Vaultkeeper');
  await captureBanker('external-banker');

  await page.getByLabel('Banker mode', { exact: true }).selectOption('Local');
  await page.getByLabel('Deposit location', { exact: true }).waitFor();
  await page.getByLabel('Banker character', { exact: true }).fill('Muir');
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  await page.locator('label').filter({ hasText: 'Deposit wait (seconds)' }).locator('input:disabled').waitFor();
  assert.equal(await page.getByLabel('Banker map ID', { exact: true }).count(), 0);
  await captureBanker('public-local-banker');

  await page.getByLabel('Banker character', { exact: true }).fill('');
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  assert.equal(await page.getByLabel('Banker character', { exact: true }).inputValue(), '');
  await captureBanker('public-blank-banker');
  assert.deepEqual(errors, []);
  console.log(`Three banker screenshots captured in ${directory}`);
} finally {
  await browser.close();
}
