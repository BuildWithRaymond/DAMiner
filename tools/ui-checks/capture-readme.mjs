import { chromium } from 'playwright';
import { execFileSync } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';

// README images must come from the simulation host, with fictional accounts.
// Generate public artwork separately so private sprite files are never pictured.
const baseUrl = process.env.GOLD_PREVIEW_URL ?? 'http://127.0.0.1:5186';
assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(new URL(baseUrl).hostname));
const output = path.resolve('docs/screenshots');
const branding = path.resolve('artifacts/readme-branding', randomUUID());
execFileSync('powershell.exe', ['-NoProfile', '-File', 'tools/Initialize-Branding.ps1', '-Directory', branding]);
await fs.mkdir(output, { recursive: true });

const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1280, height: 920 }, deviceScaleFactor: 1 });
const errors = [];
page.on('pageerror', error => errors.push(error.message));
await page.route('**/_content/DAGoldManager.UI/images/gold-bar.png', route => route.fulfill({
  path: path.join(branding, 'src/DAGoldManager.UI/wwwroot/images/gold-bar.png'), contentType: 'image/png'
}));

async function capture(name) {
  // Move off navigation controls so hover styling does not resemble a second selection.
  await page.mouse.move(1279, 0);
  await page.evaluate(() => window.scrollTo(0, 0));
  // This known simulation page contains no live profile. Check visible text and
  // password fields again before writing an image that will enter public history.
  const text = await page.locator('body').innerText();
  assert.ok(text.includes('Fictional characters only'));
  assert.equal(await page.locator('.bar-mark img').evaluate(image => image.naturalWidth), 64,
    'README must display the generated public artwork, not a private game sprite.');
  assert.match(text, /DAMiner v\d+\.\d+\.\d+/, 'Footer must show a real version, not a Razor expression.');
  const fictionalNames = ['Aisling', 'Rowan', 'Celandine', 'Muir'];
  for (const name of await page.locator('.character-name').allTextContents()) {
    assert.ok(fictionalNames.includes(name.trim()));
  }
  for (const field of await page.locator('input[type="password"]').all()) {
    assert.equal(await field.inputValue(), '');
  }
  await page.screenshot({ path: path.join(output, `${name}.png`), fullPage: name !== 'history', animations: 'disabled' });
}

try {
  await page.goto(baseUrl);
  await page.getByRole('heading', { name: 'Operations', exact: true }).waitFor();
  await page.getByRole('button', { name: 'Settings', exact: true }).click();
  await page.getByLabel('Banker mode', { exact: true }).selectOption('Local');
  await page.getByLabel('Banker character', { exact: true }).fill('Muir');
  await page.getByRole('button', { name: 'Save settings', exact: true }).click();
  await page.getByLabel('Deposit location', { exact: true }).waitFor();
  await capture('settings');

  await page.getByRole('button', { name: 'Characters', exact: true }).click();
  await page.getByRole('checkbox', { name: 'Can craft bars: Rowan', exact: true }).uncheck();
  await page.getByRole('row').filter({ has: page.getByText('Rowan', { exact: true }) })
    .getByText('Gold donor', { exact: true }).waitFor();
  await capture('characters');

  await page.getByRole('button', { name: 'Operations', exact: true }).click();
  // Blazor updates asynchronously; wait for the destination before taking the picture.
  await page.getByRole('heading', { name: 'Operations', exact: true }).waitFor();
  await capture('operations');
  await page.getByRole('button', { name: 'Go', exact: false }).click();
  await page.getByRole('heading', { name: 'Completed', exact: true }).waitFor({ timeout: 30000 });
  await page.getByRole('button', { name: 'History', exact: true }).click();
  await page.getByRole('heading', { name: 'Verified operations', exact: true }).waitFor();
  assert.ok(await page.getByRole('cell', { name: 'Verified', exact: true }).count() > 0);
  await capture('history');
  assert.deepEqual(errors, []);
  console.log('Captured four sanitized README screenshots with original public artwork.');
} finally {
  await browser.close();
}
