// Sprawdza, czy reguła walidacji blokuje zmianę typu wdrożonego pola w UI.
// PW_FIELD  — nazwa pola (domyślnie Ilosc)
// PW_TYPE   — docelowa pozycja z listy „Typ pola” (domyślnie Text)
import { chromium } from 'playwright';

const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';
const BASE = process.env.PW_BASE || 'https://localhost:5031';
const FIELD = process.env.PW_FIELD || 'Ilosc';
const TYPE = process.env.PW_TYPE || 'Text';

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
  try { await page.goto(`${BASE}/CustomField_ListView`, { waitUntil: 'domcontentloaded' }); } catch { }
  await page.waitForTimeout(4000);
  if (page.url().includes('LoginPage')) {
    await page.locator('input[type=text]').first().fill('Admin');
    await page.locator('button[data-action-name="Zaloguj się"]').first().click();
    await page.waitForTimeout(5000);
  }
  if (await page.locator('tr').count() > 3) break;
}
console.log('lista:', page.url(), '|', await page.title());

const row = page.locator('tr', { hasText: FIELD }).first();
await row.waitFor({ timeout: 20000 });
await row.dblclick();
await page.waitForTimeout(4000);
console.log('szczegoly:', page.url());
await page.screenshot({ path: `${OUT}/straznik-1-przed.png` });

console.log('--- pola na formularzu ---');
console.log(await page.evaluate(() => (document.body.innerText||'').split('\n').map(s=>s.trim()).filter(Boolean).slice(0,50).join('\n')));
console.log('--- inputy ---');
console.log(await page.evaluate(() => Array.from(document.querySelectorAll('input,select')).map(e=>`${e.tagName} id=${e.id} value="${e.value}" class=${e.className}`).join('\n')));

// „Typ pola” to combo DevExpressa — szukamy go po biezacej wartosci (PW_FROM).
const FROM = process.env.PW_FROM || 'Decimal';
const comboId = await page.evaluate((from) => {
  const el = Array.from(document.querySelectorAll('input')).find(e => e.value === from);
  return el ? el.id : null;
}, FROM);
console.log('combo id:', comboId);
await page.locator(`#${comboId}`).click();
await page.waitForTimeout(2500);
await page.screenshot({ path: `${OUT}/straznik-1b-lista.png` });
await page.getByText(TYPE, { exact: true }).last().click();
await page.waitForTimeout(3000);
console.log('typ po zmianie:', await page.locator('input.dxbl-text-edit-input').evaluateAll(els => els.map(e => e.value).join(' | ')));
await page.screenshot({ path: `${OUT}/straznik-2-typ-zmieniony.png` });

const save = page.locator('button[data-action-name="Zapisz"], button:has-text("Zapisz")').first();
await save.click();
await page.waitForTimeout(6000);
await page.screenshot({ path: `${OUT}/straznik-3-po-zapisie.png`, fullPage: true });

const text = await page.evaluate(() => document.body.innerText);
console.log('\n--- TRESC STRONY PO ZAPISIE ---');
console.log(text.split('\n').map(s => s.trim()).filter(Boolean).slice(0, 80).join('\n'));

console.log('\nzrzuty w', OUT);
await b.close();
