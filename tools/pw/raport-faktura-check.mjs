// Dowod dla wydruku faktury: ten sam zapisany raport otwarty na dwa sposoby.
//   PW_MODE=inplace  — akcja „Pokaz na raporcie" na zaznaczonym wierszu (ma dac 1 dokument)
//   PW_MODE=lista    — otwarty z listy Raportow (szablon: po jednym dokumencie na kazda fakture)
import { chromium } from 'playwright';

const BASE = process.env.PW_BASE || 'https://localhost:5031';
const MODE = process.env.PW_MODE || 'inplace';
const REPORT = process.env.PW_REPORT || 'Faktura FV/2026/08/001';
const ROW = process.env.PW_ROW || 'FV/2026/08/001';
const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';

const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const p = await ctx.newPage();
const shot = async n => { await p.screenshot({ path: `${OUT}/${n}.png`, fullPage: true }); console.log('shot ->', `${OUT}/${n}.png`); };

await p.goto(`${BASE}/`, { waitUntil: 'networkidle' });
if (await p.locator('button[data-action-name="Zaloguj się"]').count()) {
  await p.locator('input[type=text]').first().fill('Admin');
  await p.locator('button[data-action-name="Zaloguj się"]').first().click();
  await p.waitForTimeout(7000);
}

// Podsumowanie dokumentu: ile stron widzi przegladarka i jakie numery faktur na nich sa.
const podsumuj = async () => {
  const txt = await p.evaluate(() => document.body.innerText);
  const strony = [...txt.matchAll(/Strona\s+(\d+)\s+z\s+(\d+)/g)].map(m => `${m[1]}/${m[2]}`);
  const numery = [...txt.matchAll(/nr\s+(FV\/\d{4}\/\d{2}\/\d{3})/g)].map(m => m[1]);
  const tytuly = [...txt.matchAll(/^(Faktura[^\n]*)$/gm)].map(m => m[1].trim());
  console.log('STOPKI STRON:', JSON.stringify(strony));
  console.log('NUMERY W NAGLOWKACH:', JSON.stringify(numery));
  console.log('TYTULY DOKUMENTU:', JSON.stringify([...new Set(tytuly)].slice(0, 6)));
};

if (MODE === 'inplace') {
  await p.goto(`${BASE}/Faktura_ListView`, { waitUntil: 'domcontentloaded' }).catch(() => {});
  await p.waitForTimeout(8000);
  // zaznaczenie przez checkbox, zeby nie wejsc w widok szczegolow ani w link do klienta
  await p.locator('tr').filter({ hasText: ROW }).first().locator('input[type=checkbox]').first()
    .click({ timeout: 20000, force: true });
  await p.waitForTimeout(2500);
  await shot('rap-inplace-01-zaznaczony');
  await p.locator('[data-action-name="Pokaż na raporcie"] button').last().click({ timeout: 20000 });
  await p.waitForTimeout(2000);
  console.log('MENU RAPORTOW:', JSON.stringify([...new Set(await p.evaluate(() => Array.from(
    document.querySelectorAll('.dxbl-dropdown-body [role=menuitem], .dxbl-dropdown-body li')).map(e => e.innerText.trim()).filter(Boolean)))]));
  await shot('rap-inplace-02-menu');
  await p.getByText(REPORT, { exact: true }).last().click({ timeout: 20000 });
} else {
  await p.goto(`${BASE}/ReportDataV2_ListView`, { waitUntil: 'domcontentloaded' }).catch(() => {});
  await p.waitForTimeout(8000);
  console.log('URL listy raportow:', p.url());
  await shot('rap-lista-01-lista');
  await p.locator('td').filter({ hasText: new RegExp('^' + REPORT.replace(/\//g, '\\/') + '$') }).first()
    .click({ timeout: 20000 });
  await p.waitForTimeout(6000);
  console.log('AKCJE:', JSON.stringify([...new Set(await p.evaluate(() => Array.from(
    document.querySelectorAll('[data-action-name]')).map(e => e.getAttribute('data-action-name'))))]));
  await shot('rap-lista-02-otwarty');
}

await p.waitForTimeout(15000);
console.log('URL:', p.url());
await shot(`rap-${MODE}-03-dokument`);
await podsumuj();
await b.close();
