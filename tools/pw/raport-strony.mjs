// Otwiera zapisany raport w podgladzie (klucz ReportDataV2) i robi zrzut kazdej strony.
// Numer strony wpisujemy w pole „x of y" nad podgladem — tekst dokumentu jest rysowany
// w SVG, wiec innerText go nie widzi i dowodem sa zrzuty.
import { chromium } from 'playwright';

const BASE = process.env.PW_BASE || 'https://localhost:5031';
const KEY = process.env.PW_KEY;
const TAG = process.env.PW_TAG || 'raport';
const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';
if (!KEY) { console.error('podaj PW_KEY'); process.exit(1); }

const b = await chromium.launch({ headless: true });
const ctx = await b.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1500, height: 1100 } });
const p = await ctx.newPage();

await p.goto(`${BASE}/`, { waitUntil: 'networkidle' });
if (await p.locator('button[data-action-name="Zaloguj się"]').count()) {
  await p.locator('input[type=text]').first().fill('Admin');
  await p.locator('button[data-action-name="Zaloguj się"]').first().click();
  await p.waitForTimeout(7000);
}
await p.goto(`${BASE}/ReportViewer_DetailView/${KEY}`, { waitUntil: 'domcontentloaded' }).catch(() => {});

const pageBox = p.locator('input').filter({ hasText: '' });
let value = '';
for (let i = 0; i < 30; i++) {
  await p.waitForTimeout(2000);
  value = await p.evaluate(() => {
    const el = Array.from(document.querySelectorAll('input')).find(i => /\d+\s+of\s+\d+/.test(i.value || ''));
    return el ? el.value.trim() : '';
  });
  if (value) break;
}
console.log(`### ${TAG} (${KEY}) — licznik stron: "${value}"`);
const total = parseInt(value.split(/of/)[1] || '1', 10);

for (let i = 1; i <= total; i++) {
  if (i > 1) {
    const box = p.locator('input').nth(await p.evaluate(() => Array.from(document.querySelectorAll('input'))
      .findIndex(x => /\d+\s+of\s+\d+/.test(x.value || ''))));
    await box.click();
    await box.press('Control+a');
    await box.fill(String(i));
    await box.press('Enter');
    await p.waitForTimeout(6000);
  }
  await p.screenshot({ path: `${OUT}/${TAG}-str${i}.png` });
  console.log(`zrzut strony ${i} -> ${OUT}/${TAG}-str${i}.png`);
}
await b.close();
