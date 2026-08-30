// Weryfikacja raportu inplace na widoku faktur — wlasna instancja przegladarki,
// zeby zaden inny agent nie przelaczyl karty w trakcie testu.
import { chromium } from 'playwright';

const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';
const BASE = 'https://localhost:5031';
const WANT = process.env.PW_REPORT || null;

const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const page = await ctx.newPage();
const shot = async n => { await page.screenshot({ path: `${OUT}/${n}.png` }); console.log('shot ->', `${OUT}/${n}.png`); };

await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
if (page.url().includes('LoginPage')) {
  await page.locator('input[type=text]').first().fill('Admin');
  await page.locator('button[data-action-name="Zaloguj się"]').first().click();
  await page.waitForLoadState('networkidle');
}
try { await page.goto(`${BASE}/Faktura_ListView`, { waitUntil: 'domcontentloaded' }); } catch (e) { console.log('goto retry:', e.message.split('\n')[0]); await page.waitForTimeout(2000); }
await page.waitForTimeout(3000);
await page.waitForTimeout(2500);
console.log('url przed klikiem:', page.url(), '|', await page.title());
await shot('01-lista');
for (let i=0;i<5;i++){ try { await page.locator('tr', { hasText: 'FV/2026/08/001' }).first().click({ timeout: 12000 }); break; } catch(e){ console.log('retry klik wiersza', i); await page.waitForTimeout(3000);} }
await page.waitForTimeout(1200);
await shot('02-wiersz-zaznaczony');

for (let i=0;i<6;i++){ if (await page.locator('[data-action-name="Pokaż na raporcie"] button.dxbl-btn-split-dropdown').count()) break; console.log('czekam na akcje raportow', i); await page.waitForTimeout(3000); }
await page.locator('[data-action-name="Pokaż na raporcie"] button.dxbl-btn-split-dropdown').first().click({ timeout: 20000 });
await page.waitForTimeout(1500);
await shot('03-menu-raportow');
const items = await page.evaluate(() => Array.from(
  document.querySelectorAll('.dxbl-dropdown-body [role=menuitem], .dxbl-dropdown-body li, dxbl-dropdown [role=menuitem], [class*=dropdown] [class*=item]'))
  .map(e => e.innerText.trim()).filter(t => t && t.length < 120));
console.log('POZYCJE MENU:', JSON.stringify([...new Set(items)]));

if (WANT) {
  await page.getByText(WANT, { exact: true }).last().click({ timeout: 20000 });
  await page.waitForTimeout(12000);
  console.log('po wyborze:', page.url(), '|', await page.title());
  await shot('04-raport-inplace');
}
await b.close();
