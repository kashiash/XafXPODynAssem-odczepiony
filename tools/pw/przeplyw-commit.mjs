// Dowod na to, ze przejscie w akcji „Zmien stan" utrwala rekord bez recznego „Zapisz".
// Skrypt tylko klika przejscie i konczy — stan w bazie sprawdzamy osobnym SELECT-em.
import { chromium } from 'playwright';

const BASE = process.env.PW_BASE || 'https://localhost:5031';
const VIEW = process.env.PW_VIEW || 'Faktura_ListView';
const ROW = process.env.PW_ROW || 'FV/2026/08/002';
const WANT = process.env.PW_TRANSITION || 'Wystawiona';
const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';

const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const p = await ctx.newPage();
const shot = async n => { await p.screenshot({ path: `${OUT}/${n}.png` }); console.log('shot ->', `${OUT}/${n}.png`); };

await p.goto(`${BASE}/`, { waitUntil: 'networkidle' });
if (await p.locator('button[data-action-name="Zaloguj się"]').count()) {
  await p.locator('input[type=text]').first().fill('Admin');
  await p.locator('button[data-action-name="Zaloguj się"]').first().click();
  await p.waitForTimeout(6000);
}
try { await p.goto(`${BASE}/${VIEW}`, { waitUntil: 'domcontentloaded' }); } catch (e) { console.log('goto retry'); }
await p.waitForTimeout(7000);

// Klikamy KOMORKE z numerem, nie caly wiersz: w wierszu jest link do Klienta i klikniecie
// w losowe miejsce potrafi otworzyc widok Customer zamiast faktury.
await p.locator('td').filter({ hasText: new RegExp('^' + ROW.replace(/[/]/g, '\\/') + '$') }).first().click({ timeout: 20000 });
await p.waitForTimeout(4000);
console.log('otwarty widok:', p.url());
await shot('wf-01-widok');

for (let i = 0; i < 6; i++) {
  if (await p.locator('[data-action-name="Zmień stan"]').count()) break;
  console.log('czekam na akcje Zmien stan', i); await p.waitForTimeout(2500);
}
await p.locator('[data-action-name="Zmień stan"] button').last().click({ timeout: 20000 });
await p.waitForTimeout(1500);
await shot('wf-02-menu');
console.log('PRZEJSCIA:', JSON.stringify([...new Set(await p.evaluate(() => Array.from(
  document.querySelectorAll('.dxbl-dropdown-body [role=menuitem], .dxbl-dropdown-body li')).map(e => e.innerText.trim()).filter(Boolean)))]));

await p.getByText(WANT, { exact: true }).last().click({ timeout: 20000 });
await p.waitForTimeout(4000);
await shot('wf-03-po-przejsciu');
console.log('AKCJE PO PRZEJSCIU (czy "Zapisz" wisi):', JSON.stringify([...new Set(await p.evaluate(
  () => Array.from(document.querySelectorAll('[data-action-name]')).map(e => e.getAttribute('data-action-name'))))]));
console.log('NIE klikam Zapisz. Koniec.');
await b.close();
