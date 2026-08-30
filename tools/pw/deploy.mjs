// Klika akcje „Deploy Schema” na liscie klas uzytkownika.
// Aplikacja konczy sie kodem 42, a petla uruchomieniowa podnosi ja z nowym DLL-em.
import { chromium } from 'playwright';

const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';
const BASE = process.env.PW_BASE || 'https://localhost:5031';

const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const page = await ctx.newPage();

await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
if (page.url().includes('LoginPage')) {
  await page.locator('input[type=text]').first().fill('Admin');
  await page.locator('button[data-action-name="Zaloguj się"]').first().click();
  await page.waitForLoadState('networkidle');
}

for (let i = 0; i < 3; i++) {
  try { await page.goto(`${BASE}/CustomClass_ListView`, { waitUntil: 'domcontentloaded' }); } catch { }
  await page.waitForTimeout(4000);
  if (page.url().includes('LoginPage')) {
    await page.locator('input[type=text]').first().fill('Admin');
    await page.locator('button[data-action-name="Zaloguj się"]').first().click();
    await page.waitForTimeout(5000);
  }
  if (await page.locator('button:has-text("Deploy Schema")').count()) break;
}
console.log('lista klas:', page.url(), '|', await page.title());

await page.locator('button:has-text("Deploy Schema")').first().click();
await page.waitForTimeout(2000);
await page.screenshot({ path: `${OUT}/deploy-1-potwierdzenie.png` });
const ok = page.locator('button:has-text("OK"), button:has-text("Tak")').first();
if (await ok.count()) { await ok.click(); }
await page.waitForTimeout(8000);
await page.screenshot({ path: `${OUT}/deploy-2-po.png` });
console.log('kliknieto Deploy Schema');
await b.close();
