import { chromium } from 'playwright';
const BASE = process.env.PW_BASE || 'https://localhost:5031';
const VIEW = process.env.PW_VIEW || 'Faktura_ListView';
const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';
const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const p = await ctx.newPage();
await p.goto(`${BASE}/`, { waitUntil: 'networkidle' });
if (await p.locator('button[data-action-name="Zaloguj się"]').count()) {
  await p.locator('input[type=text]').first().fill('Admin');
  await p.locator('button[data-action-name="Zaloguj się"]').first().click();
  await p.waitForTimeout(6000);
}
console.log('po logowaniu:', p.url());
try { await p.goto(`${BASE}/${VIEW}`, { waitUntil: 'domcontentloaded' }); } catch (e) { console.log('goto retry'); }
await p.waitForTimeout(7000);
console.log('URL:', p.url());
console.log('AKCJE:', JSON.stringify([...new Set(await p.evaluate(() => Array.from(document.querySelectorAll('[data-action-name]')).map(e => e.getAttribute('data-action-name'))))]));
await p.screenshot({ path: `${OUT}/probe.png` });
await b.close();
