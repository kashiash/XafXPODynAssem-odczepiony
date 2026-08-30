// Rozmowa z asystentem AI we wlasnej instancji przegladarki.
// Wiadomosci podajemy w PW_MSGS rozdzielone znakiem "||".
import { chromium } from 'playwright';

const OUT = process.env.PW_OUT || '/private/tmp/claude-501/-Users-jacek-Projects-Brekhof/08514d06-b088-4331-8de3-0ed0f18d18bf/scratchpad/pw';
const BASE = 'https://localhost:5031';
const MSGS = (process.env.PW_MSGS || '').split('||').map(s => s.trim()).filter(Boolean);

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
  try { await page.goto(`${BASE}/AIChat_DetailView`, { waitUntil: 'domcontentloaded' }); } catch { }
  await page.waitForTimeout(3000);
  if (page.url().includes('LoginPage')) {
    await page.locator('input[type=text]').first().fill('Admin');
    await page.locator('button[data-action-name="Zaloguj się"]').first().click();
    await page.waitForTimeout(4000);
  }
  if (await page.locator('textarea').count()) break;
}
await page.waitForTimeout(2000);
console.log('chat url:', page.url(), '|', await page.title());

const readAll = () => page.evaluate(() =>
  Array.from(document.querySelectorAll('[class*=chatui-message]')).map(e => e.innerText.trim()).filter(Boolean));

for (const [i, msg] of MSGS.entries()) {
  const before = (await readAll()).length;
  await page.locator('textarea').first().fill(msg);
  await page.locator('textarea').first().press('Enter');
  console.log(`\n>>> USER: ${msg}`);
  // czekamy, az tresc czatu przestanie sie zmieniac przez 12 s (max 4 min)
  let prev = '', stable = 0;
  for (let t = 0; t < 80; t++) {
    await page.waitForTimeout(3000);
    const now = (await readAll()).join('\n');
    if (now === prev && now.length > 0) { stable++; if (stable >= 4) break; } else { stable = 0; prev = now; }
  }
  const all = await readAll();
  console.log(`<<< ASYSTENT: ${all[all.length - 1]}`);
  await page.screenshot({ path: `${OUT}/chat-${i + 1}.png` });
}
console.log('\nzrzuty w', OUT);
await b.close();
